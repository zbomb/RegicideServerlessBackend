using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using Amazon.Lambda.Core;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

using Regicide.API;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LogoutFunction
{
    public class Function
    {
        static AmazonDynamoDBClient Database;
        static TokenManager TokenProvider;
        static string TableName;

        // Secret json structure
        struct Config
        {
            public string table { get; set; }
            public string sigkey { get; set; }
            public string salt { get; set; }
        }

        public Function()
        {
            Database = new AmazonDynamoDBClient();

            // Load secrets
            using( var Secrets = new AmazonSecretsManagerClient() )
            {
                var SecTask = Secrets.GetSecretValueAsync( new GetSecretValueRequest { SecretId = "api/salt" } );
                SecTask.Wait();

                var SecResult = SecTask.Result;
                var Serializer = new Amazon.Lambda.Serialization.Json.JsonSerializer();
                Config LoadedConfig;

                if( SecResult.SecretString != null )
                {
                    using( var Stream = new MemoryStream( Encoding.UTF8.GetBytes( SecResult.SecretString ) ) )
                    {
                        Stream.Position = 0;
                        LoadedConfig = Serializer.Deserialize<Config>( Stream );
                    }
                }
                else
                {
                    using( SecResult.SecretBinary )
                    {
                        LoadedConfig = Serializer.Deserialize<Config>( SecResult.SecretBinary );
                    }
                }

                // Validate and retrieve values
                if( String.IsNullOrEmpty( LoadedConfig.sigkey ) || String.IsNullOrEmpty( LoadedConfig.table ) )
                    throw new Exception( "Failed to read config from secret manager!" );

                byte[] SigKey = Encoding.UTF8.GetBytes( LoadedConfig.sigkey );
                if( SigKey.Length < 64 )
                    throw new Exception( "Failed to read sigkey from secrets manager!" );

                TableName = LoadedConfig.table;
                TokenProvider = new TokenManager( SigKey );
            }
        }

        ~Function()
        {
            Database?.Dispose();
        }

        public LogoutResponse FunctionHandler( LogoutRequest Input, ILambdaContext Context )
        {
            return Input.AuthToken == null ? new LogoutResponse { Result = LogoutResult.Error } : PerformLogout( Input );
        }

        public LogoutResponse PerformLogout( LogoutRequest Input )
        {
            // The authorizer has already validated this token for us, so we just need to clear the token
            // identifier from the database and respond to the user
            var Token = TokenProvider.ReadToken( Input.AuthToken );
            if( Token == null )
            {
                // Couldnt read the token!
                return new LogoutResponse { Result = LogoutResult.InvalidToken };
            }

            var UpReq = new UpdateItemRequest()
            {
                TableName = TableName,
                Key =
                {
                    { "User", new AttributeValue { S = Token.UserId.ToLower() } },
                    { "Property", new AttributeValue { N = ( (int) AccountEntry.BasicInfo ).ToString() }  }
                },
                UpdateExpression = "REMOVE #t",
                ExpressionAttributeNames = { { "#t", "Token" } },
                ConditionExpression = "#t = :in_token",
                ExpressionAttributeValues = { { ":in_token", new AttributeValue { S = Token.TokenId } } }
            };

            try
            {
                var UpTask = Database.UpdateItemAsync( UpReq );
                UpTask.Wait();
            }
            catch( AggregateException AEx )
            {
                if( AEx.InnerException is ConditionalCheckFailedException )
                {
                    return new LogoutResponse { Result = LogoutResult.Success };
                }

                LambdaLogger.Log( "[ERROR] An exception was thrown while updating database (clear authtoken)" );
                LambdaLogger.Log( AEx.ToString() );

                return new LogoutResponse { Result = LogoutResult.Error };
            }
            catch( ConditionalCheckFailedException )
            {
                // User had an old token, so we will reply with an OK signal, leave the existing token in the database
                // This way, the user will forget the token they currently have, and the current token will remain in use
                // until the next login
                return new LogoutResponse { Result = LogoutResult.Success };
            }
            catch( Exception Ex )
            {
                LambdaLogger.Log( "[ERROR] An exception was thrown while updating database (clear authtoken)" );
                LambdaLogger.Log( Ex.ToString() );

                return new LogoutResponse { Result = LogoutResult.Error };
            }

            return new LogoutResponse { Result = LogoutResult.Success };

        }
    }
}

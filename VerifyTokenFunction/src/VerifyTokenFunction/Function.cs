using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

using Regicide.API;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace VerifyTokenFunction
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

        public struct VerifyRequest
        {
            public string AuthToken;
        }

        public struct VerifyResponse
        {
            public bool Result;
        }


        public VerifyResponse FunctionHandler( VerifyRequest Input, ILambdaContext Context )
        {
            // We need to not only verify that the token is signed, but we need to also verify
            // that its the token currently associated with the user, the user-id is available in
            // the token data, so we wont explicitly request it
            return String.IsNullOrWhiteSpace( Input.AuthToken ) ? new VerifyResponse { Result = false } : PerformVerify( Input );
        }

        public VerifyResponse PerformVerify( VerifyRequest Input )
        {
            // Were going to have the validator enabled, so were ensured the token with be valid
            var Token = TokenProvider.ReadToken( Input.AuthToken );
            if( Token == null )
            {
                // Couldnt read the token!
                return new VerifyResponse { Result = false };
            }

            var GetReq = new GetItemRequest()
            {
                TableName = TableName,
                Key =
                {
                    { "User", new AttributeValue { S = Token.UserId.ToLower() } },
                    { "Property", new AttributeValue { N = ( (int) AccountEntry.BasicInfo ).ToString() } }
                },
                ProjectionExpression = "#t",
                ExpressionAttributeNames = { { "#t", "Token" } }
            };

            var GetTask = Database.GetItemAsync( GetReq );
            GetTask.Wait();

            var Result = GetTask.Result;
            if( !Result.IsItemSet || !Result.Item.Any() || !Result.Item.ContainsKey( "Token" ) )
            {
                return new VerifyResponse { Result = false };
            }

            // Check for token id equality
            return Result.Item[ "Token" ].S.Equals( Token.TokenId )
                ? new VerifyResponse { Result = true }
                : new VerifyResponse { Result = false };

        }
    }
}

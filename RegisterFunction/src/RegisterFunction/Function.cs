using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text;

using Amazon.Lambda.Core;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

using Amazon.DynamoDBv2;

using Amazon.S3;
using Amazon.S3.Model;

using Regicide.API;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace RegisterFunction
{
    public class Function
    {
        // Persistent Values
        static string TableName;
        static byte[] PassSalt;
        static TokenManager TokenProvider;
        static AmazonDynamoDBClient Database;
        static Account DefaultAccount;

        // Secret json structure
        struct Config
        {
            public string table { get; set; }
            public string sigkey { get; set; }
            public string salt { get; set; }
        }


        /*====================================================================================
         *  Initialization - Revised
         *  - Previously, Init was syncronous, where each needed service was
         *    accessed one at a time, waiting for the last to finish
         *  - Now, initialization will happen in parrallel, and, config has all been placed 
         *    in secret manager, so we perform 1/3 the number of API calls
         ====================================================================================*/
        public Function()
        {
            // Create Database Instance
            Database = new AmazonDynamoDBClient();

            // Build secret manager request
            var SecReq = new GetSecretValueRequest() { SecretId = "api/salt" };

            // Build S3 request
            var ActReq = new GetObjectRequest()
            {
                BucketName = "regicide-config",
                Key = "default-account.json"
            };

            using( var Secrets = new AmazonSecretsManagerClient() )
            {
                using( var S3 = new AmazonS3Client() )
                {
                    // Send requests to AWS API
                    var SecTask = Secrets.GetSecretValueAsync( SecReq );
                    var ActTask = S3.GetObjectAsync( ActReq );

                    // Wait for both to finish
                    Task.WaitAll( SecTask, ActTask );

                    var SecResult = SecTask.Result;
                    var ActResult = ActTask.Result;

                    // Handle Configuration
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
                    if( String.IsNullOrEmpty( LoadedConfig.salt ) || String.IsNullOrEmpty( LoadedConfig.sigkey ) || String.IsNullOrEmpty( LoadedConfig.table ) )
                        throw new Exception( "Failed to read config from secret manager!" );

                    PassSalt = Encoding.UTF8.GetBytes( LoadedConfig.salt );
                    if( PassSalt.Length < 32 )
                        throw new Exception( "Failed to read password salt from secrets manager!" );

                    byte[] SigKey = Encoding.UTF8.GetBytes( LoadedConfig.sigkey );
                    if( SigKey.Length < 64 )
                        throw new Exception( "Failed to read sigkey from secrets manager!" );
                        
                    TokenProvider   = new TokenManager( SigKey );
                    TableName       = LoadedConfig.table;

                    // Retrieve and deserialize default account from S3
                    if( ActResult?.ResponseStream == null || ActResult.ResponseStream.Length == 0 )
                        throw new Exception( "Failed to read account defaults from S3!" );

                    using( ActResult.ResponseStream )
                    {
                        DefaultAccount = Serializer.Deserialize<Account>( ActResult.ResponseStream );
                    }

                    if( DefaultAccount?.Info == null )
                        throw new Exception( "Failed to deserialize account defaults from S3!" );
                }
            }
        }


        ~Function()
        {
            Database?.Dispose();
        }


        public RegisterResponse FunctionHandler( RegisterRequest Input, ILambdaContext Context )
        {
            return Input.Username == null || Input.DispName == null || Input.PassHash == null || Input.Email == null
                ? new RegisterResponse { Result = RegisterResult.Error, Token = null, Account = null }
                : PerformRegister( Input );
        }

        public RegisterResponse PerformRegister( RegisterRequest Input )
        {
            string Username = Input.Username.ToLower();
            string Password = Input.PassHash;
            string Email = Input.Email;
            string DispName = Input.DispName;

            if( PassSalt == null || PassSalt.Length < 32 )
                throw new Exception( "Password salt not set!" ); // Throw exception when this container is bad

            // Check if password appears valid, otherwise it will throw an exception
            Span<byte> _1 = new Span<byte>();
            if( String.IsNullOrWhiteSpace( Password ) || 
               !Convert.TryFromBase64String( Password, _1, out int _2 ) )
            {
                return new RegisterResponse()
                {
                    Result = RegisterResult.BadPassHash,
                    Account = null,
                    Token = null
                };
            }

            // Generate Auth Token
            string AuthToken = null;
            string TokenId = null;
            try
            {
                AuthToken = GenerateAuthToken( Username, out TokenId );

                if( String.IsNullOrWhiteSpace( AuthToken ) )
                    throw new Exception( "Generated token was null/empty" );
            }
            catch( Exception Ex )
            {
                LambdaLogger.Log( String.Format( "[WARNING] GenerateAuthToken threw an exception! {0}", Ex ) );

                return new RegisterResponse()
                {
                    Result = RegisterResult.Error,
                    Token = null,
                    Account = null
                };
            }

            // Build Register Request
            var Request = new RegisterArguments()
            {
                Username = Username,
                PassHash = Password,
                DispName = DispName,
                EmailAdr = Email,
                TokenId = TokenId,
                DynTable = TableName,
                PassSalt = PassSalt,
                Database = Database,
                Default = DefaultAccount
            };

            var Res = Registration.RegisterAccount( Request, out Account NewAccount );

            return Res != RegisterResult.Success
                ? new RegisterResponse()
                {
                    Result = Res,
                    Token = null,
                    Account = null
                }
                : new RegisterResponse()
                {
                    Result = RegisterResult.Success,
                    Token = AuthToken,
                    Account = NewAccount
                };
        }


        string GenerateAuthToken( string inUser, out string OutId )
        {
            if( TokenProvider == null )
                throw new Exception( "Token provider is null! Check config" ); // Bad container

            AuthToken NewToken = new AuthToken
            {
                Issued = DateTime.UtcNow.Ticks,
                Expiration = ( DateTime.UtcNow + new TimeSpan( 365, 0, 0, 0, 0 ) ).Ticks,
                UserId = inUser,
                TokenId = TokenProvider.GenerateTokenId()
            };

            // Give back the token id to be added to the database
            OutId = NewToken.TokenId;
            return TokenProvider.BuildToken( NewToken );
        }
    }
}

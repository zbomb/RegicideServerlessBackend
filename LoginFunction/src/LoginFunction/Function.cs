using System;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

using Amazon.Lambda.Core;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

using Amazon.DynamoDBv2;

using Regicide.API;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LoginFunction
{
    public class Function
    {
        // Database
        static AmazonDynamoDBClient Database;
        static string TableName;

        // Authorization
        static TokenManager TokenProvider;
        static byte[] PassSalt;

        struct Config
        {
            public string table { get; set; }
            public string sigkey { get; set; }
            public string salt { get; set; }
        }

        public Function()
        {
            // Create database instance
            Database = new AmazonDynamoDBClient();

            // Load config from secret manager
            using( var Secrets = new AmazonSecretsManagerClient() )
            {
                var SecTask = Secrets.GetSecretValueAsync( new GetSecretValueRequest { SecretId = "api/salt" } );
                SecTask.Wait();

                var SecResult   = SecTask.Result;
                var Serializer  = new Amazon.Lambda.Serialization.Json.JsonSerializer();
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

                TokenProvider = new TokenManager( SigKey );
                TableName = LoadedConfig.table;
            }
        }


        ~Function()
        {
            Database?.Dispose();
        }

        /*===============================================================================
         *  Function Entry Point
        ===============================================================================*/
        public LoginResponse Login( LoginRequest Request, ILambdaContext Context )
        {
            return Request.PassHash == null || Request.Username == null
                ? new LoginResponse { Result = LoginResult.BadRequest, Account = null, AuthToken = null }
                : PerformLogin( Request );
        }


        /*======================================================================================
         *  PerformLogin( LoginRequest )
         * 
         *   Handles the login request from function entry, attempts login for the user, 
         *   generates a user credential key and responds with the user id, access token
         *   and with a return value to indicate the actual result of login
         ======================================================================================*/
        LoginResponse PerformLogin( LoginRequest Request )
        {
            // Generate a new token for this user
            // When a new token is generated, the old token will be no longer usable
            // Since we store the salt used in verification with the account info,
            // the old token will fail verification because the salt will be incorrect
            var AuthToken = GenerateAuthToken( Request.Username.ToLower(), out string outId );

            if( String.IsNullOrWhiteSpace( AuthToken ) )
                throw new Exception( "Generated token was null/empty" );

            var LoginReq = new LoginParameters()
            {
                Username = Request.Username,
                PassHash = Request.PassHash,
                TokenId  = outId,
                DynTable = TableName,
                PassSalt = PassSalt,
                Database = Database
            };

            // Request appears to be valid.. so lets lookup user info in the database
            var Result = RegDatabase.PerformLogin( LoginReq, out Account LoadedAccount );

            if( Result == RegDatabase.LoginResult.Error )
            {
                return new LoginResponse()
                {
                    Result = LoginResult.DatabaseError,
                    Account = null,
                    AuthToken = null
                };
            }
            if( Result == RegDatabase.LoginResult.Invalid )
            {
                return new LoginResponse()
                {
                    Result = LoginResult.InvalidCredentials,
                    Account = null,
                    AuthToken = null
                };
            }

            // Success!
            return new LoginResponse()
            {
                Result = LoginResult.Success,
                Account = LoadedAccount,
                AuthToken = AuthToken
            };
        }


        /*===============================================================================
        *  string GenerateAuthToken()
        * 
        *   Generates a new, unique AuthToken that the user will send with future
        *   requests to access their account. This token can be stored indefinatley, 
        *   until the user calls Logout, or their password is changed.
        ===============================================================================*/
        string GenerateAuthToken( string inUser, out string outId )
        {
            if( TokenProvider == null )
                throw new Exception( "Token provider is null! Check config" );

            AuthToken NewToken = new AuthToken
            {
                Issued = DateTime.UtcNow.Ticks,
                Expiration = ( DateTime.UtcNow + new TimeSpan( 365, 0, 0, 0, 0 ) ).Ticks,
                UserId = inUser,
                TokenId = TokenProvider.GenerateTokenId()
            };

            // Give the token id back so we can insert it into the database
            outId = NewToken.TokenId;
            return TokenProvider.BuildToken( NewToken );
        }



    }
}

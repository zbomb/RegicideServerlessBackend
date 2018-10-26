using System;
using System.Text;
using System.Data;
using System.IO;

using Amazon.Lambda.Core;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
 
using MySql.Data.MySqlClient;

using Regicide.Auth;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LoginFunction
{
    public class Function
    {
        // Database
        static string ConnectionString;

        // Token Provider
        static TokenManager TokenProvider;

        // Constant Values
        static readonly int UsernameMinLength   = 5;
        static readonly int UsernameMaxLength   = 32;
        static readonly int PasswordHashLength  = 44;


        public Function()
        {
            LambdaLogger.Log( "[Login] RUNNING CONSTRUCTOR" );
            // Build Connection String
            var SqlBuilder = new MySqlConnectionStringBuilder
            {
                Server = DecryptConfigValue( "db_endpoint" ),
                UserID = DecryptConfigValue( "db_user" ),
                Password = DecryptConfigValue( "db_password" ),
                Port = Convert.ToUInt32( DecryptConfigValue( "db_port" ) ),
                Database = DecryptConfigValue( "db_schema" ),
                MinimumPoolSize = 0,
                MaximumPoolSize = 1,
                Pooling = true,
                CharacterSet = "utf8mb4"
            };

            ConnectionString = SqlBuilder.ToString();

            byte[] SignatureKey = Encoding.UTF8.GetBytes( DecryptConfigValue( "auth_sigkey" ) );
            if( SignatureKey == null || SignatureKey.Length < 32 )
            {
                LambdaLogger.Log( "[Login CRITICAL] Failed to read signature key from the config values!" );
            }
            else
            {
                // Make sure key is exactly 64 bytes
                byte[] FinalKey = new byte[ 32 ];
                Array.ConstrainedCopy( SignatureKey, 0, FinalKey, 0, 32 );

                TokenProvider = new TokenManager( FinalKey );
            }
        }


        static string DecryptConfigValue( string inName )
        {
            var EncryptedData = Convert.FromBase64String( Environment.GetEnvironmentVariable( inName ) );

            using( var KeyClient = new AmazonKeyManagementServiceClient() )
            {
                var Request = new DecryptRequest()
                {
                    CiphertextBlob = new MemoryStream( EncryptedData )
                };

                var ResponseTask = KeyClient.DecryptAsync( Request );
                ResponseTask.Wait();

                var Response = ResponseTask.Result;

                using( var DataStream = Response.Plaintext )
                {
                    string Output = Encoding.UTF8.GetString( DataStream.ToArray() );

                    if( Output == null )
                        throw new Exception( "Output was null!" );

                    return Output;
                }
            }
        }

        /*===============================================================================
         *  Function Entry Point
        ===============================================================================*/
        public LoginResponse Login( LoginRequest Request, ILambdaContext Context )
        {
            return PerformLogin( Request );
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
            string Username = Request.Username;
            string Password = Request.PassHash;

            // Perform basic validation to try and catch easy failures before sending request over to database
            if( String.IsNullOrWhiteSpace( Username ) || String.IsNullOrWhiteSpace( Password ) ||
               Username.Length < UsernameMinLength || Username.Length > UsernameMaxLength ||
               Password.Length != PasswordHashLength )
            {
                return new LoginResponse()
                {
                    Result = LoginResult.BadRequest,
                    Identifier = 0,
                    AuthToken = ""
                };
            }

            // Request appears to be valid.. so lets pass the request along to the database
            bool bLookupResult = false;
            UInt32 Identifier = 0;

            try
            {
                bLookupResult = LookupAccountIdentifier( Username, Password, out Identifier );
            }
            catch( Exception )
            {
                // The exception should already be logged by the function, so we just need to respond to the client here
                return new LoginResponse()
                {
                    Result = LoginResult.DatabaseError,
                    Identifier = 0,
                    AuthToken = ""
                };
            }

            // Check if the credentials were not found
            if( !bLookupResult )
            {
                return new LoginResponse()
                {
                    Result = LoginResult.InvalidCredentials,
                    Identifier = 0,
                    AuthToken = ""
                };
            }

            // Generate a new token for this user
            // When a new token is generated, the old token will be no longer usable
            // Since we store the salt used in verification with the account info,
            // the old token will fail verification because the salt will be incorrect
            string AuthToken = null;
            try
            {
                AuthToken = GenerateAuthToken( Identifier );

                if( String.IsNullOrWhiteSpace( AuthToken ) )
                    throw new Exception( "Generated token was null/empty" );
            }
            catch( Exception Ex )
            {
                LambdaLogger.Log( String.Format( "[Login WARNING] GenerateAuthToken threw an exception! {0}", Ex.ToString() ) );

                return new LoginResponse()
                {
                    Result = LoginResult.DatabaseError,
                    Identifier = 0,
                    AuthToken = ""
                };
            }

            // Success!
            return new LoginResponse()
            {
                Result = LoginResult.Success,
                Identifier = Identifier,
                AuthToken = AuthToken
            };
        }

        /*===============================================================================
         *  LookupAccountIdentifier( Username, Password, out Id )
         * 
         *  Performs an account lookup in the database, and returns the account id
         *  for the specified account, if the password hash is correct
        ===============================================================================*/
        bool LookupAccountIdentifier( string inUser, string inPass, out UInt32 Identifier )
        {
            using( var Connection = new MySqlConnection( ConnectionString ) )
            {
                // Ensure the database connection is opened
                try
                {
                    if( Connection.State == ConnectionState.Broken )
                        Connection.Close();

                    Connection.Open();
                
                }
                catch( Exception Ex )
                {
                    LambdaLogger.Log( string.Format( "[Login ERROR] Failed to connect to database! {0}", Ex.Message ) );
                    throw;
                }

                LambdaLogger.Log( "[Login] CONNECTED AT LookupAccountIdentifier" );

                // Execute stored procedure for user login
                using( var Command = new MySqlCommand( "AttemptLogin", Connection ) )
                {
                    Command.CommandType = CommandType.StoredProcedure;
                    Command.Parameters.AddWithValue( "@inUsername", inUser ).Direction = ParameterDirection.Input;
                    Command.Parameters.AddWithValue( "@inPassword", inPass ).Direction = ParameterDirection.Input;

                    Command.Parameters.Add( "@outResult", MySqlDbType.Int32 ).Direction = ParameterDirection.Output;
                    Command.Parameters.Add( "@outIdentifier", MySqlDbType.UInt32 ).Direction = ParameterDirection.Output;

                    try
                    {
                        Command.ExecuteNonQuery();
                    }
                    catch( Exception Ex )
                    {
                        LambdaLogger.Log( string.Format( "[Login ERROR] Exception was thrown while performing login procedure! {0}", Ex.Message ) );
                        throw;
                    }

                    LambdaLogger.Log( "[Login] RAN QUERY AT LookupAccountIdentifier" );

                    int Result = Convert.ToInt32( Command.Parameters[ "@outResult" ].Value );

                    // Check for abnormal error
                    if( Result == (int) DatabaseLookupResult.Error )
                    {
                        throw new Exception( "Unknown database error!" );
                    }

                    // Check for invalid credentials
                    if( Result == (int) DatabaseLookupResult.Invalid )
                    {
                        Identifier = 0;
                        return false;
                    }

                    // Login was successful!
                    Identifier = Convert.ToUInt32( Command.Parameters[ "@outIdentifier" ].Value );
                    return true;
                }
            }
        }


        /*===============================================================================
        *  string GenerateAuthToken()
        * 
        *   Generates a new, unique AuthToken that the user will send with future
        *   requests to access their account. This token can be stored indefinatley, 
        *   until the user calls Logout, or their password is changed.
        ===============================================================================*/
        string GenerateAuthToken( UInt32 inUser )
        {
            if( TokenProvider == null )
                throw new Exception( "Token provider is null! Check config" );

            AuthToken NewToken = new AuthToken
            {
                Issued = DateTime.UtcNow.Ticks,
                Expiration = ( DateTime.UtcNow + new TimeSpan( 365, 0, 0, 0, 0 ) ).Ticks,
                UserId = inUser
            };

            // Generate a unique identifier string for this auth token
            // This will be tied to this users account
            // If, for some reason, we generate an id thats already in use, we will loop again and generate a new one
            // Once the token is definatley unique, the procedure will update the account with it, and return success code
            string Salt = TokenProvider.GenerateTokenSalt();

            using( var Database = new MySqlConnection( ConnectionString ) )
            {
                Database.Open();

                LambdaLogger.Log( "[Login] CONNECTED AT GenerateAuthToken" );

                using( var Command = new MySqlCommand( "UPDATE Accounts SET ActiveToken=@salt WHERE Identifier=@uid LIMIT 1", Database ) )
                {
                    Command.Parameters.AddWithValue( "@salt", Salt );
                    Command.Parameters.AddWithValue( "@uid", inUser );

                    try
                    {
                        Command.ExecuteNonQuery();
                    }
                    catch( Exception )
                    {
                        // Fails if we cant set the active token, since its needed to verify
                        return null;
                    }

                    LambdaLogger.Log( "[Login] COMPLETED QUERY AT GenerateAuthToken" );
                }
            }

            return TokenProvider.BuildToken( NewToken, Salt );
        }



    }
}

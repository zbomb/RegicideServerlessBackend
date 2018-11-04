using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

using Regicide.API;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace RegicideAuthorizer
{
    public class Function
    {
        static TokenManager TokenProvider;

        // Secret json structure
        struct Config
        {
            public string table { get; set; }
            public string sigkey { get; set; }
            public string salt { get; set; }
        }

        public Function()
        {
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
                if( String.IsNullOrEmpty( LoadedConfig.sigkey ) )
                    throw new Exception( "Failed to read config from secret manager!" );

                byte[] SigKey = Encoding.UTF8.GetBytes( LoadedConfig.sigkey );
                if( SigKey.Length < 64 )
                    throw new Exception( "Failed to read sigkey from secrets manager!" );
                
                TokenProvider = new TokenManager( SigKey );
            }
        }

        public APIGatewayCustomAuthorizerResponse FunctionHandler( APIGatewayCustomAuthorizerRequest Input, ILambdaContext Context )
        {
            // Validate user token
            bool bAuth = false;
            AuthToken Token = null;

            if( !String.IsNullOrWhiteSpace( Input.AuthorizationToken ) )
            {
                bAuth = TokenProvider.ValidateSignature( Input.AuthorizationToken );

                if( bAuth )
                {
                    // If desire, we can also perform user specific tasks by getting the username from the token
                    Token = TokenProvider.ReadToken( Input.AuthorizationToken );
                    if( Token == null || String.IsNullOrWhiteSpace( Token.UserId ) || Token.UserId.Length < Constants.UsernameMinLength || Token.UserId.Length > Constants.UsernameMaxLength )
                    {
                        // Bad Token? 
                        bAuth = false;
                        Token = null;
                    }
                }
            }

            // Create policy to allow user to access the API method
            var Policy = new APIGatewayCustomAuthorizerPolicy()
            {
                Version = "2012-10-17",
                Statement = new List<APIGatewayCustomAuthorizerPolicy.IAMPolicyStatement>
                {
                    new APIGatewayCustomAuthorizerPolicy.IAMPolicyStatement()
                    {
                        Action = new HashSet<string>( new string[] { "execute-api:Invoke" } ),
                        Effect = bAuth ? "Allow" : "Deny",
                        Resource = new HashSet<string>( new string[] { Input.MethodArn } )
                    }
                }
            };

            var AuthContext = new APIGatewayCustomAuthorizerContextOutput
            {
                [ "User" ] = bAuth ? Token.UserId : "User",
                [ "Path" ] = Input.MethodArn,
                [ "Permissions" ] = bAuth ? BuildPermissions( Token ) : String.Empty
            };

            // Final Response
            return new APIGatewayCustomAuthorizerResponse()
            {
                PrincipalID = bAuth ? "API" : "User",
                Context = AuthContext,
                PolicyDocument = Policy,
                UsageIdentifierKey = "Public"
            };
        }

        public string BuildPermissions( AuthToken inUser )
        {
            // For now, all users will have general persmissions
            return string.Join( '|', 'g' );
        }
    }
}

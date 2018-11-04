using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

using Amazon.Lambda.Serialization.Json;
using Amazon.Lambda.Core;

/*-----------------------------------------------------------------------------------------------------------
 *  Auth Tokens
 * 
 *  These tokens will be used to authenticate access to regicide resources
 *  The basic token format is quite simple, and similar to JWT
 *  <Token Info>.<User Info>.<Signature>
 *  
 *  The tokens will probably be for the most part, fixed size, but should be treated as a
 *  variable sized string just in case, as well as future proofing 
 * 
 *  To use this library, simply take the token string, and pass it into 
 *  TokenManager.ValidateToken to check format, and the signing.
 *  Then, you can pass it along to TokenManager.ReadToken to get back an AuthToken object
 *  that contains all the fields needed
 * 
 *  If you need to build a token, you can either build it using a preset TokenId or
 *  let the token manager generate a token id on its own. But, the token manager will
 *  check the database to ensure that the token is indeed unique (hopfully). There could
 *  potentially be a duplicate, if two identifical tokens are generated simultaneously, or
 *  before the first token is inserted into the database, but the probability is very low
-----------------------------------------------------------------------------------------------------------*/

namespace Regicide.API
{
    public struct AuthTokenInfo
    {
        public long expr;
        public long issued;
    }

    public struct AuthUserInfo
    {
        public string acc_id;
        public string token_id;
    }

    public class AuthToken
    {
        public string UserId { get; set; }
        public long Expiration { get; set; }
        public long Issued { get; set; }
        public string TokenId { get; set; }

        public AuthToken( string inUser, DateTime inExpr, DateTime inIssued, string tokenId )
        {
            UserId = inUser;
            Expiration = inExpr.Ticks;
            Issued = inIssued.Ticks;
            TokenId = tokenId;
        }

        public AuthToken( string inUser, long inExpr, long inIssued, string tokenId )
        {
            UserId = inUser;
            Expiration = inExpr;
            Issued = inIssued;
            TokenId = tokenId;
        }

        public AuthToken()
        {
            UserId = String.Empty;
            Expiration = 0;
            Issued = 0;
            TokenId = String.Empty;
        }
    }

    public class TokenManager
    {
        readonly byte[] Key;
        static readonly string TokenFormat = "^[A-Za-z0-9+/=]+.[A-Za-z0-9+/=]+.[A-Za-z0-9+/=]+$";
        static readonly string AllowedIdChars = "qwertyuiopasdfghjklzxcvbnmQWERTYUIOPASDFGHJKLZXCVBNM1234567890";

        public TokenManager( byte[] SignatureKey )
        {
            Key = SignatureKey;
        }


        public bool ValidateSignature( string inToken )
        {
            // First, lets check the basic format of the token
            if( !ValidateFormat( inToken ) )
                return false;

            if( Key.Length < 64 )
                throw new Exception( "SigKey must be at least 64 bytes!" );

            // Now we can split the token up since we know its in the proper format
            string[] Chunks = inToken.Split( '.' );

            if( Chunks.Length != 3 )
                return false;

            try
            {
                byte[] TokenInfo    = Convert.FromBase64String( Chunks[ 0 ] );
                byte[] UserInfo     = Convert.FromBase64String( Chunks[ 1 ] );
                byte[] Signature    = Convert.FromBase64String( Chunks[ 2 ] );

                // Validate the signature using the key we have
                using( var HMac = new HMACSHA256( Key ) )
                {
                    // Compute the hash of the token info + user info encoded as utf8 (no '.' character)

                    byte[] TokenData = new byte[ TokenInfo.Length + UserInfo.Length ];
                    TokenInfo.CopyTo( TokenData, 0 );
                    UserInfo.CopyTo( TokenData, TokenInfo.Length );

                    byte[] TokenHash = HMac.ComputeHash( TokenData );

                    if( TokenHash.Length != Signature.Length )
                        return false;

                    for( int i = 0; i < TokenHash.Length;  i++ )
                    {
                        if( TokenHash[ i ] != Signature[ i ] )
                            return false;
                    }
                }
            }
            catch( Exception )
            {
                return false;
            }

            // Token is valid!
            return true;
        }


        public bool ValidateFormat( string inToken )
        {
            return !String.IsNullOrWhiteSpace( inToken ) && Regex.IsMatch( inToken, TokenFormat);
        }


        public AuthToken ReadToken( string inToken )
        {
            // Reads the actual data stored in the token, but doesnt verify signature, only format
            // Call VerifyToken before using this function if you need to ensure the token is valid
            if( !ValidateFormat( inToken ) )
                return null;

            string[] Chunks = inToken.Split( '.' );

            if( Chunks.Length != 3 )
                return null;

            byte[] TokenData;
            byte[] UserData;

            // Convert the fields to byte arrays
            try
            {
                TokenData = Convert.FromBase64String( Chunks[ 0 ] );
                UserData = Convert.FromBase64String( Chunks[ 1 ] );
            }
            catch( Exception )
            {
                return null;
            }

            // Deserialize back into structures from json
            AuthTokenInfo TokenInfo;
            AuthUserInfo UserInfo;

            try
            {
                var Parser = new JsonSerializer();
                TokenInfo = Parser.Deserialize<AuthTokenInfo>( new MemoryStream( TokenData ) );
                UserInfo = Parser.Deserialize<AuthUserInfo>( new MemoryStream( UserData ) );
            }
            catch( Exception )
            {
                return null;
            }

            // We wont verify contents, leave that for the caller to implement
            return new AuthToken( UserInfo.acc_id, TokenInfo.expr, TokenInfo.issued, UserInfo.token_id );
        }


        public string BuildToken( AuthToken Input )
        {
            // Ensure we arent being passed null values, beyond that, the caller is responsible for the 
            // content of the token, as long as it doesnt cause us to error
            if( Input == null || Input.Expiration < DateTime.UtcNow.Ticks )
                return null;

            if( Key.Length < 64 )
                throw new Exception( "TokenProvider key size too small" );

            // First, we need to split the data into two chunks, one for user info and the 
            // other is for token info
            AuthTokenInfo TokenInfo = new AuthTokenInfo()
            {
                expr = Input.Expiration,
                issued = Input.Issued
            };

            AuthUserInfo UserInfo = new AuthUserInfo()
            {
                acc_id = Input.UserId,
                token_id = Input.TokenId
            };

            // Serialize the chunks using json
            byte[] TokenChunk = null;
            byte[] UserChunk = null;

            try
            {
                var Parser = new JsonSerializer();

                using( var Stream = new MemoryStream() )
                {
                    Parser.Serialize( TokenInfo, Stream );
                    TokenChunk = Stream.ToArray();
                }

                using( var Stream = new MemoryStream() )
                {
                    Parser.Serialize( UserInfo, Stream );
                    UserChunk = Stream.ToArray();
                }

                if( TokenChunk == null || UserChunk == null )
                    throw new Exception();
            }
            catch( Exception )
            {
                return null;
            }

            // Now, we need to compute the signature to get the final chunk
            byte[] SigChunk = null;

            using( var HMac = new HMACSHA256( Key ) )
            {
                byte[] TokenData = new byte[ TokenChunk.Length + UserChunk.Length ];
                TokenChunk.CopyTo( TokenData, 0 );
                UserChunk.CopyTo( TokenData, TokenChunk.Length );

                SigChunk = HMac.ComputeHash( TokenData );
            }

            if( SigChunk == null )
                return null;

            // Build chunks into final token
            return String.Format( "{0}.{1}.{2}", Convert.ToBase64String( TokenChunk ), Convert.ToBase64String( UserChunk ), Convert.ToBase64String( SigChunk ) );
        }


        public string GenerateTokenId()
        {
            byte[] RandomBytes = new byte[ 1 ];
            using( var RNG = new RNGCryptoServiceProvider() )
            {
                RNG.GetNonZeroBytes( RandomBytes );
                RandomBytes = new byte[ 16 ];
                RNG.GetNonZeroBytes( RandomBytes );
            }

            var Output = new StringBuilder();

            foreach( byte b in RandomBytes )
            {
                Output.Append( AllowedIdChars[ b % (AllowedIdChars.Length) ] );
            }

            return Output.ToString();
        }


    }
}
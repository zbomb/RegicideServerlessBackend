using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

using Amazon.Lambda.Core;

namespace Regicide.API
{
    public enum RegisterResult
    {
        Error = 0,
        InvalidUsername = 1,
        InvalidDispName = 2,
        InvalidEmail = 3,
        UsernameTaken = 4,
        EmailExists = 5,
        BadPassHash = 6,
        Success = 7
    }

    public class RegisterArguments
    {
        public string Username { get; set; }
        public string PassHash { get; set; }
        public string DispName { get; set; }
        public string EmailAdr { get; set; }
        public string TokenId { get; set; }
        public byte[] PassSalt { get; set; }
        public string DynTable { get; set; }
        public AmazonDynamoDBClient Database { get; set; }
        public Account Default { get; set; }
    }

    public static class Registration
    {
        // Parameter Constraints
        static readonly int UsernameMinLength   = 5;
        static readonly int UsernameMaxLength   = 32;
        //static readonly int PasswordHashLength  = 44;
        static readonly int EmailMinLength      = 3;
        static readonly int EmailMaxLength      = 255;
        static readonly int DispNameMinLength   = 5;
        static readonly int DispNameMaxLength   = 48;


        public static RegisterResult RegisterAccount( RegisterArguments Args, out Account NewAccount )
        {
            // Validate Arguments
            // Exceptions are thrown when the container is bad (ie. failed to init properly)
            if( Args.PassSalt == null || Args.PassSalt.Length < 32 )
                throw new Exception( "Salt not specified in the function config!" );

            if( Args.Default?.Info == null )
                throw new Exception( "Default account not set!" );

            if( Args.Database == null )
                throw new Exception( "DynamoDB client is null!" );

            // Convert username to lowercase
            string inUser = Args.Username?.ToLower();
            NewAccount = null;

            // Basic Parameter Validation
            if( String.IsNullOrWhiteSpace( inUser ) || inUser.Length < UsernameMinLength || inUser.Length > UsernameMaxLength )
                return RegisterResult.InvalidUsername;
            if( String.IsNullOrWhiteSpace( Args.PassHash ) || Args.PassHash.Length < 40 )
                return RegisterResult.BadPassHash;
            if( String.IsNullOrWhiteSpace( Args.DispName ) || Args.DispName.Length < DispNameMinLength || Args.DispName.Length > DispNameMaxLength )
                return RegisterResult.InvalidDispName;
            if( String.IsNullOrWhiteSpace( Args.EmailAdr ) || Args.EmailAdr.Length < EmailMinLength || Args.EmailAdr.Length > EmailMaxLength || !Args.EmailAdr.Contains( "@" ) )
                return RegisterResult.InvalidEmail;
            if( String.IsNullOrWhiteSpace( Args.TokenId ) )
                return RegisterResult.Error;
            if( String.IsNullOrWhiteSpace( Args.DynTable ) )
                throw new Exception( "Table Name is null!" );

            // Ensure username is alpha numeric, allow underscores and hypens, but not spaces
            if( !Regex.IsMatch( inUser, "^[a-zA-Z0-9_-]+$" ) )
                return RegisterResult.InvalidUsername;

            // Allow all printable, non control characters
            if( Regex.IsMatch( Args.DispName, @"\p{C}+" ) )
                return RegisterResult.InvalidDispName;

            // Also check display name for trailing or leading whitespace
            if( !Args.DispName.TrimStart().Equals( Args.DispName ) || !Args.DispName.TrimEnd().Equals( Args.DispName ) )
                return RegisterResult.InvalidDispName;

            // Ensure password is valid base64 string
            if( !Regex.IsMatch( Args.PassHash, "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$" ) )
                return RegisterResult.BadPassHash;

            // Thats about all the parameter validation were going to do, for the most part were going to be quite flexible

            // Additional hash pass on password to disociate database entry, and value sent by clients
            string FinalPass = Utils.HashPassword( Args.PassHash, Args.PassSalt );
            if( ( FinalPass?.Length ?? 0 ) < 40 )
                return RegisterResult.Error;

            var newAccount = new Account
            {
                Info = new BasicAccountInfo()
                {
                    Username = inUser,
                    Coins = Args.Default.Info.Coins,
                    DisplayName = Args.DispName,
                    Email = Args.EmailAdr,
                    Verified = Args.Default.Info.Verified
                },
                Cards = Args.Default.Cards,
                Decks = Args.Default.Decks,
                Achievements = Args.Default.Achievements,
                PrivInfo = null
            };

            var SerializedBasicInfo = Serializer.SerializeBasicInfo( newAccount.Info, FinalPass, Args.TokenId );

            var PutReq = new PutItemRequest()
            {
                TableName = Args.DynTable,
                ConditionExpression = "attribute_not_exists( #u )",
                ExpressionAttributeNames =
                {
                    { "#u", "User" }
                },
                Item = SerializedBasicInfo
            };

            Task<PutItemResponse> PutTask;
            bool bAlreadyExists = false;

            try
            {
                PutTask = Args.Database.PutItemAsync( PutReq );
                PutTask.Wait();
            }
            catch( Exception Ex )
            {
                if( Ex is ConditionalCheckFailedException CEx )
                {
                    bAlreadyExists = true;
                }
                else if( Ex is AggregateException AgEx )
                {
                    if( AgEx.InnerException is ConditionalCheckFailedException InnerCEx )
                    {
                        bAlreadyExists = true;
                    }
                    else
                    {
                        LambdaLogger.Log( "[ERROR] Exception thrown while putting item in database!" );
                        LambdaLogger.Log( Ex.ToString() );
                        return RegisterResult.Error;
                    }
                }
                else
                {
                    LambdaLogger.Log( "[ERROR] Exception thrown while putting item in database!" );
                    LambdaLogger.Log( Ex.ToString() );
                    return RegisterResult.Error;
                }
            }

            // Check if the account already existed
            if( bAlreadyExists )
            {
                return RegisterResult.UsernameTaken;
            }

            // Now were going to query for the email, to check if theres duplicates, if found, we
            // will have to rollback the original insertion. This could potentially miss an email, since were
            // not able to query with ConsistentRead enabled, if another user signs up simultaneously w/ same email, we could miss it
            var EmailQuery = new QueryRequest()
            {
                TableName = Args.DynTable,
                IndexName = "Email-index",
                ProjectionExpression = "Email",
                KeyConditionExpression = "#e = :in_email",
                FilterExpression = "#u <> :in_user",
                ExpressionAttributeNames = { { "#e", "Email" }, { "#u", "User"} },
                ExpressionAttributeValues = 
                { 
                    { ":in_email", new AttributeValue { S = Args.EmailAdr.ToLower() } },
                    { ":in_user", new AttributeValue { S = inUser } } 
                },
                Limit = 2
            };

            Task<QueryResponse> QueryTask;
            try
            {
                QueryTask = Args.Database.QueryAsync( EmailQuery );
                QueryTask.Wait();
            }
            catch( Exception Ex )
            {
                LambdaLogger.Log( "[ERROR] An exception was thrown while performing query on database!" );
                LambdaLogger.Log( Ex.ToString() );
                return RegisterResult.Error;
            }

            var QueryResults = QueryTask.Result;
            if( QueryResults.Count > 0 )
            {
                // Duplicate Emails Detected! Were going to rollback everything weve done so far and return the EmailExists code
                var DeleteReq = new DeleteItemRequest()
                {
                    TableName = Args.DynTable,
                    Key =
                    {
                        { "User", new AttributeValue { S = inUser } },
                        { "Property", new AttributeValue { N = ( (int) AccountEntry.BasicInfo ).ToString() } }
                    }
                };

                // Call it and forget
                try
                {
                    Args.Database.DeleteItemAsync( DeleteReq );
                }
                catch( Exception Ex )
                {
                    LambdaLogger.Log( "[ERROR] An exception was thrown while deleting from databse (duplicate email rollback)" );
                    LambdaLogger.Log( Ex.ToString() );
                }

                return RegisterResult.EmailExists;
            }

            // Build the rest of the account
            if( !Serializer.SerializeCards( newAccount.Cards, inUser, out var LowerSet, out var UpperSet ) )
            {
                // Bad Container
                throw new Exception( "Failed to serialize default card set!" );
            }

            var DeckList = Serializer.SerializeDecks( newAccount.Decks, inUser );
            var AchvList = Serializer.SerializeAchievements( newAccount.Achievements, inUser );

            // Build a batch write request to add the rest of this account to the database
            var RemainingWrites = new Dictionary<string, List<WriteRequest>>
            {
                { Args.DynTable, new List<WriteRequest>() }
            };

            if( ( LowerSet?.Count ?? 0 ) > 0 )
            {
                RemainingWrites[ Args.DynTable ].Add( new WriteRequest( new PutRequest( LowerSet ) ) );
            }

            if( ( UpperSet?.Count ?? 0 ) > 0 )
            {
                RemainingWrites[ Args.DynTable ].Add( new WriteRequest( new PutRequest( UpperSet ) ) );
            }

            if( ( DeckList?.Count ?? 0 ) > 0 )
            {
                foreach( var D in DeckList )
                {
                    if( ( D?.Count ?? 0 ) > 0 )
                    {
                        RemainingWrites[ Args.DynTable ].Add( new WriteRequest( new PutRequest( D ) ) );
                    }
                }
            }

            if( ( AchvList?.Count ?? 0 ) > 0 )
            {
                RemainingWrites[ Args.DynTable ].Add( new WriteRequest( new PutRequest( AchvList ) ) );
            }

            // Process Batch Request
            int RetryCount = 0;
            int RetryTime = 100;
            int LastAdd = 200;

            // We wont check for timeout manually, since the lambda system automatically checks for timeouts
            // Unfortunatley, if this function times-out we could have a partially written account
            while( RemainingWrites.Count > 0 )
            {
                BatchWriteItemResponse BatchResult;

                try
                {
                    var BatchTask = Args.Database.BatchWriteItemAsync( RemainingWrites );
                    BatchTask.Wait();
                    BatchResult = BatchTask.Result;
                }
                catch( Exception Ex )
                {
                    LambdaLogger.Log( "[ERROR] An exception was thrown while performing batch write! (writing new account)" );
                    LambdaLogger.Log( Ex.ToString() );
                    return RegisterResult.Error;
                }

                // If everything was written, then stop write loop
                if( BatchResult.UnprocessedItems.Count == 0 )
                    break;

                // Update remaining items
                RetryCount++;
                RemainingWrites = BatchResult.UnprocessedItems;

                // Decaying wait
                Thread.Sleep( RetryTime );
                RetryTime += Math.Min( LastAdd / 2, 400 );
                LastAdd = (int)( LastAdd / 1.5d );
            }

            // All account entries added!
            NewAccount = newAccount;
            return RegisterResult.Success;
        }

    }
}

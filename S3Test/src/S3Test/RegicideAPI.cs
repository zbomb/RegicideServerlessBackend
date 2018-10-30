using System;
using System.Collections.Generic;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DocumentModel;


namespace Regicide.API
{
    public class Account
    {
        public BasicAccountInfo Info { get; set; }
        public List<Card> Cards { get; set; }
        public List<Deck> Decks { get; set; }
        public List<Achievement> Achievements { get; set; }

        public Account()
        {
            Info = null;
            Cards = null;
            Decks = null;
            Achievements = null;
        }
    }

    public class BasicAccountInfo
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public UInt64 Coins { get; set; }
        public string DisplayName { get; set; }
        public bool Verified { get; set; }
    }

    public class Card
    {
        public UInt16 Identifier { get; set; }
        public UInt16 Count { get; set; }
    }

    public class Deck
    {
        public UInt16 Identifier { get; set; }
        public string DisplayName { get; set; }

        public List<Card> Cards { get; set; }
    }

    public class Achievement
    {
        public UInt16 Identifier { get; set; }
        public bool Complete { get; set; }
        public Int32 State { get; set; }
    }

    public enum AccountProperty
    {
        Info = 0,
        Cards = 1,
        Decks = 2,
        Achievements = 3,
        All = 4
    }


    /*===============================================================================
     *      API.Database
     *  - Database library that abstracts away the database implementation
     *  - All data operations needed to interact with Regicide are contained
     *    within this library
    ================================================================================*/
    public static class Database
    {
        private enum AccountEntry
        {
            BasicInfo = 0,
            CardsLower = 1,
            CardsUpper = 2,
            Achievements = 3,
            Reserved = 4
            // Number 4 is reserved for future use
            // Anything thats > 4 is a deck with the Id = PropertyIndex - 4, PropertyIndex = Id + 4
            // This allows us to store all decks, as properties of the main account
        }

        // Were using this as a member, to allow the connection to stay open between API calls
        // This way were not waiting for the connection to open on every invocation
        static AmazonDynamoDBClient DatabaseClient;

        public static void Init()
        {
            if( DatabaseClient != null )
            {
                return;
            }

            DatabaseClient = new AmazonDynamoDBClient();
        }

        public static void Dispose()
        {
            DatabaseClient?.Dispose();
            DatabaseClient = null;
        }


        /*######################################################################################################################
         *                  QUERYING
        #####################################################################################################################*/

        public static object QueryProperty( string Username, AccountProperty Property )
        {
            try
            {
                if( Property == AccountProperty.All )
                {
                    Console.WriteLine( "[RegAPI] Cant call QueryProperty with the All property! Use QueryPropertyList instead!" );
                    return null;
                }

                if( String.IsNullOrWhiteSpace( Username ) )
                    return null;

                if( DatabaseClient == null )
                    Init();

                // We need to process decks a little differently, since we need to use a query
                // instead of a get item request
                if( Property == AccountProperty.Decks )
                {
                    var Request = new QueryRequest()
                    {
                        TableName = "Accounts",
                        KeyConditionExpression = "#u = :in_user and #p > 4",
                        ExpressionAttributeNames =
                        {
                            { "#u", "User" },
                            { "#p", "Property" }
                        },
                        ExpressionAttributeValues =
                        {
                            { ":in_user", new AttributeValue { S = Username.ToLower() } }  
                        },
                        ConsistentRead = true
                    };

                    var QueryTask = DatabaseClient.QueryAsync( Request );
                    QueryTask.Wait();

                    var Results = QueryTask.Result;
                    var Output = new List<Deck>();

                    // If there is no decks for this user, return an empty deck list
                    if( Results == null || Results.Count <= 0 )
                        return Output;

                    // Iterate through and build a list
                    foreach( var DeckInfo in Results.Items )
                    {
                        var NewDeck = ProcessDeck( DeckInfo, Username );
                        if( NewDeck != null )
                            Output.Add( NewDeck );
                    }

                    // Return output
                    return Output;
                }
                else if( Property == AccountProperty.Cards )
                {
                    var Request = new QueryRequest()
                    {
                        TableName = "Accounts",
                        KeyConditionExpression = "#u = :in_user and ( #p = 1 or #p = 2 )",
                        ExpressionAttributeNames =
                        {
                            { "#u", "User" },
                            { "#p", "Property" }
                        },
                        ExpressionAttributeValues =
                        {
                            { ":in_user", new AttributeValue { S = Username.ToLower() } }
                        },
                        ConsistentRead = true
                    };

                    var QueryTask = DatabaseClient.QueryAsync( Request );
                    QueryTask.Wait();

                    var Results = QueryTask.Result;

                    if( Results == null || Results.Count <= 0 )
                    {
                        Console.WriteLine( "[RegAPI] Warning! Account '{0}' has no card properties in the database!", Username );
                        return null;
                    }

                    var Output = new List<Card>();
                    foreach( var CardSet in Results.Items )
                    {
                        var LoadedCards = ProcessCardList( CardSet, Username );
                        if( LoadedCards != null && LoadedCards.Count > 0 )
                            Output.AddRange( LoadedCards );
                    }

                    return Output;
                }
                else
                {
                    AccountEntry Entry;
                    if( Property == AccountProperty.Info )
                        Entry = AccountEntry.BasicInfo;
                    else if( Property == AccountProperty.Achievements )
                        Entry = AccountEntry.Achievements;
                    else
                    {
                        Console.WriteLine( "[RegAPI] Attempt to query for unknown/unsupported account property on {0}! Property: {1}", Username, Property.ToString() );
                        return null;
                    }

                    var GetRequest = new GetItemRequest()
                    {
                        TableName = "Accounts",
                        Key = new Dictionary<string, AttributeValue>
                        {
                            { "User", new AttributeValue { S = Username.ToLower() } },
                            { "Property", new AttributeValue { N = ( (int) Entry ).ToString() } }
                        },
                        ConsistentRead = true
                    };

                    var GetTask = DatabaseClient.GetItemAsync( GetRequest );
                    GetTask.Wait();

                    var Result = GetTask.Result;

                    // Handle results
                    return Property == AccountProperty.Info
                        ? ProcessAccountInfo( Result?.Item, Username )
                        : (object) ProcessAchievementList( Result?.Item, Username );
                }

            }
            catch( Exception Ex )
            {
                Console.WriteLine( "[RegAPI] An exception was thrown while querying an account property ({0})", Property.ToString() );
                Console.WriteLine( Ex );

                return null;
            }
        }


        public static Account QueryPropertyList( string Username, params AccountProperty[] Props )
        {
            try
            {
                if( DatabaseClient == null )
                    Init();

                if( String.IsNullOrWhiteSpace( Username ) )
                    return null;

                // Build the query expression that will be used
                string QueryExpression = "#u = :in_user and ( ";
                bool bFirst = true;
                bool bAll = false;
                foreach( var p in Props )
                {
                    if( !bFirst )
                        QueryExpression += " or ";

                    bFirst = false;

                    if( p == AccountProperty.Info )
                        QueryExpression += "#p = 0";
                    else if( p == AccountProperty.Cards )
                        QueryExpression += "#p = 1 or #p = 2";
                    else if( p == AccountProperty.Decks )
                        QueryExpression += "#p > 4";
                    else if( p == AccountProperty.Achievements )
                        QueryExpression += "#p = 3";
                    else if( p == AccountProperty.All )
                    {
                        QueryExpression = "#u = :in_user";
                        bAll = true;
                        break;
                    }
                }

                if( !bAll )
                    QueryExpression += " )";

                var Request = new QueryRequest()
                {
                    TableName = "Accounts",
                    KeyConditionExpression = QueryExpression,
                    ExpressionAttributeNames =
                {
                    { "#u", "User" },
                    { "#p", "Property" }
                },
                    ExpressionAttributeValues =
                {
                    { ":in_user", new AttributeValue { S = Username.ToLower() } }
                },
                    ConsistentRead = true,
                };

                var QueryTask = DatabaseClient.QueryAsync( Request );
                QueryTask.Wait();

                var Results = QueryTask.Result;

                // If nothing was found, return null
                if( Results == null || Results.Count <= 0 )
                    return null;

                var Output = new Account();

                // Loop through each property, pass along to proper processor, and insert results into output
                foreach( var Prop in Results.Items )
                {
                    if( Prop[ "Property" ].N == ( (int) AccountEntry.BasicInfo ).ToString() )
                    {
                        Output.Info = ProcessAccountInfo( Prop, Username );
                    }
                    else if( Prop[ "Property" ].N == ( (int) AccountEntry.CardsUpper ).ToString() ||
                            Prop[ "Property" ].N == ( (int) AccountEntry.CardsLower ).ToString() )
                    {
                        var CardList = ProcessCardList( Prop, Username );
                        if( CardList == null )
                            continue;

                        if( Output.Cards == null )
                            Output.Cards = new List<Card>();

                        Output.Cards.AddRange( CardList );
                    }
                    else if( Prop[ "Property" ].N == ( (int) AccountEntry.Achievements ).ToString() )
                    {
                        Output.Achievements = ProcessAchievementList( Prop, Username );
                    }
                    else if( Int32.Parse( Prop[ "Property" ].N ) > 4 )
                    {
                        var NewDeck = ProcessDeck( Prop, Username );

                        if( NewDeck == null )
                            continue;

                        if( Output.Decks == null )
                            Output.Decks = new List<Deck>();

                        Output.Decks.Add( NewDeck );
                    }
                }

                // Output Results
                return Output;
            }
            catch( Exception Ex )
            {
                Console.WriteLine( "[RegAPI] An exception was thrown while getting account property list for '{0}'", Username );
                Console.WriteLine( Ex );

                return null;
            }
        }

        /*==========================================================================================
         *      Internal Helpers For Processing Accounts
         *  - Each part of an account (basic info, cards, decks, achievements) is seperate
         *  - To query for a single part of this info, you can use QueryProperty
         *  - To query for multiple parts of this info, use QueryPropertyList
         *  - These functions define how each part of the account is read from the database
         *    and they are called as part of the two functions mention previously
        ==========================================================================================*/
        static BasicAccountInfo ProcessAccountInfo( Dictionary< string, AttributeValue> Result, string Username )
        {
            if( Result == null )
                return null;

            // First, we need to make sure we have all of the properties needed
            if( !Result.ContainsKey( "Email" ) ||
               !Result.ContainsKey( "Coins" ) || !Result.ContainsKey( "DispName" ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' is missing attributes in database. This API call failed!", Username.ToLower() );
                return null;
            }

            // Start parsing results
            // We need to verify that all attributes are present
            if( !UInt64.TryParse( Result[ "Coins" ].N, out UInt64 Coins ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has invalid coins set in database! This API call failed!", Username.ToLower() );
                return null;
            }
            if( String.IsNullOrWhiteSpace( Result[ "Email" ].S ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has invalid email set in database! This API call failed!", Username.ToLower() );
                return null;
            }
            if( String.IsNullOrWhiteSpace( Result[ "DispName" ].S ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has inalid Display Name set in database! This API call failed!", Username.ToLower() );
                return null;
            }
            if( String.IsNullOrWhiteSpace( Result[ "User" ].S ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has invalid username set in database! This API call failed!", Username.ToLower() );
                return null;
            }

            // Now that everything we have is validated, build the Basic Info object
            var Info = new BasicAccountInfo()
            {
                Coins = Coins,
                Email = Result[ "Email" ].S,
                DisplayName = Result[ "DispName" ].S,
                Username = Result[ "User" ].S
            };

            Info.Verified = Result.ContainsKey( "Verify" ) && Result[ "Verify" ].BOOL;

            return Info;
        }


        public static List<Card> ProcessCardList( Dictionary< string, AttributeValue> Result, string Username )
        {
            if( Result == null )
                return null;

            // Make sure we have the card list property we need
            if( !Result.ContainsKey( "Cards" ) || !Result[ "Cards" ].IsMSet )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has invalid cards set in database! This API call failed!", Username );
                return null;
            }

            // Iterate through cards and build list
            var Output = new List<Card>();

            foreach( var CardInfo in Result[ "Cards" ].M )
            {
                // Parse Card Id
                if( !UInt16.TryParse( CardInfo.Key, out UInt16 Identifier ) || Identifier <= 0 )
                {
                    Console.WriteLine( "[RegAPI] Warning! Found an invalid card in '{0}'s card list (ID)! Key: {1}", Username, CardInfo.Key );
                    continue;
                }

                // Parse Card Count
                if( !UInt16.TryParse( CardInfo.Value.N, out UInt16 Count ) || Count <= 0 )
                {
                    Console.WriteLine( "[RegAPI] Warning! Found an invalid card in '{0}'s card list (COUNT)! Value: {1}", Username, CardInfo.Value.N );
                    continue;
                }

                // Add card to list
                Output.Add( new Card()
                {
                    Identifier = Identifier,
                    Count = Count
                } );
            }

            return Output;
        }


        public static Deck ProcessDeck( Dictionary< string, AttributeValue> Result, string Username )
        {
            if( Result == null )
                return null;

            // Make sure we have needed attribytes
            if( !Result.ContainsKey( "Type" ) || !Result.ContainsKey( "DispName" ) || !Result.ContainsKey( "Cards" ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid deck set in database (MissingAttributes)", Username );
                return null;
            }

            // Process the deck info
            if( !Int32.TryParse( Result[ "Type" ].N, out int PropertyNumber ) || PropertyNumber - 4 <= 0 || ( PropertyNumber - 4 ) > UInt16.MaxValue )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid deck set in database (PropertyNumber)! Value: {1}", Username, PropertyNumber - 4 );
                return null;
            }

            if( String.IsNullOrWhiteSpace( Result[ "DispName" ].S ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid deck set in database! (DisplayName) Id: {1}", Username, PropertyNumber - 4 );
                return null;
            }

            // The deck doesnt necisarily need to contain cards, so we wont error if it doesnt
            var Output = new Deck()
            {
                Identifier = (UInt16) ( PropertyNumber - 4 ),
                DisplayName = Result[ "DispName" ].S,
                Cards = new List<Card>()
            };

            if( Result[ "Cards" ].IsMSet )
            {
                foreach( var CardInfo in Result[ "Cards" ].M )
                {
                    if( !UInt16.TryParse( CardInfo.Key, out UInt16 CardId ) || CardId <= 0 )
                    {
                        Console.WriteLine( "[RegAPI] Warning! Account '{0}' has a deck '{1}' with an invalid card! (ID) Key: {2}", Username, Output.DisplayName, CardInfo.Key );
                        continue;
                    }

                    if( !UInt16.TryParse( CardInfo.Value.N, out UInt16 Count ) || Count <= 0 )
                    {
                        Console.WriteLine( "[RegAPI] Warning! Account '{0}' has a deck '{1}' with an invalid card! (COUNT) Value: {2}", Username, Output.DisplayName, CardInfo.Value.N );
                        continue;
                    }

                    Output.Cards.Add( new Card()
                    {
                        Identifier = CardId,
                        Count = Count
                    } );
                }
            }

            return Output;
        }


        public static List< Achievement > ProcessAchievementList( Dictionary< string, AttributeValue > Result, string Username )
        {
            if( Result == null )
                return null;

            if( !Result.ContainsKey( "Achv" ) || !Result[ "Achv" ].IsLSet )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has invalid achievement list set in database! No achievements will be loaded." );
                return null;
            }

            var Output = new List<Achievement>();

            foreach( var AchvInfo in Result[ "Achv" ].L )
            {
                // Each attribute in list is a map type
                if( !AchvInfo.IsMSet )
                {
                    Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid achievement in database! (Map Null)", Username );
                    continue;
                }
                if( !AchvInfo.M.ContainsKey( "id" ) || !UInt16.TryParse( AchvInfo.M[ "id" ].N, out UInt16 AchvId ) )
                {
                    Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid achievement in database! (InvalidID)", Username );
                    continue;
                }
                if( !AchvInfo.M.ContainsKey( "cp" ) || !AchvInfo.M[ "cp" ].IsBOOLSet )
                {
                    Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid achievement in database! (Complete) Id: {1}", Username, AchvId );
                    continue;
                }
                if( !AchvInfo.M.ContainsKey( "st" ) || !Int32.TryParse( AchvInfo.M[ "st" ].N, out int State ) )
                {
                    Console.WriteLine( "[RegAPI] Warning! Account '{0}' has an invalid achievement in database! (State) Id: {1}", Username, AchvId );
                    continue;
                }

                // Check if we already have an achievement with this id
                if( Output.Exists( X => X.Identifier == AchvId ) )
                {
                    Console.WriteLine( "[RegAPI] Warning! Account '{0}' has duplicate achievements in database! Id: {1}", Username, AchvId );
                    continue;
                }

                Output.Add( new Achievement()
                {
                    Identifier = AchvId,
                    State = State,
                    Complete = AchvInfo.M[ "cp" ].BOOL
                } );
            }

            return Output;
        }


        /*######################################################################################################################
         *              SAVING
        ######################################################################################################################*/

        /*######################################################################################################################
         *              METHODS
        ######################################################################################################################*/


        public static Account PerformLogin( string Username, string PassHash )
        {
            try
            {
                // Init if it hasnt been already
                if( DatabaseClient == null )
                    Init();

                // Query table for account info, filter using password hash specified
                var Request = new QueryRequest()
                {
                    TableName = "Accounts",
                    ConsistentRead = true,
                    KeyConditionExpression = "#u = :in_user and #pr = :in_prop",
                    FilterExpression = "#pw = :in_pass",
                    ExpressionAttributeNames =
                    {
                        { "#u", "User" },
                        { "#pr", "Property" },
                        { "#pw", "PassHash" }
                    },
                    ExpressionAttributeValues =
                    {
                        { ":in_user", new AttributeValue { S = Username.ToLower() } },
                        { ":in_prop", new AttributeValue { N = ( (int) AccountEntry.BasicInfo ).ToString() } },
                        { ":in_pass", new AttributeValue { S = PassHash } }
                    }
                };

                var QueryTask = DatabaseClient.QueryAsync( Request );
                QueryTask.Wait();

                var Results = QueryTask.Result;

                // Check if we returned any results
                if( ( Results?.Count ?? 0 ) <= 0 )
                    return null;

                // Double check is password is correct, shouldnt be needed though
                if( !Results.Items[ 0 ].ContainsKey( "PassHash" ) || !Results.Items[ 0 ][ "PassHash" ].S.Equals( PassHash ) )
                    return null;

                // Now, we need to query the rest of the account information
                var Output = QueryPropertyList( Username, AccountProperty.Cards, AccountProperty.Decks, AccountProperty.Achievements );
                if( Output == null )
                {
                    Console.WriteLine( "[RegAPI] Account '{0}' sign in was successful, but full account query returned null!", Username );
                    return null;
                }

                Output.Info = ProcessAccountInfo( Results.Items[ 0 ], Username );

                if( Output.Info == null )
                {
                    Console.WriteLine( "[RegAPI] Account '{0}' sign in was successful, but failed to process basic account info!", Username );
                    return null;
                }

                return Output;

            }
            catch( Exception Ex )
            {
                Console.WriteLine( "[RegAPI] An exception was thrown while performing login on database!" );
                Console.WriteLine( Ex );

                return null;
            }
        }
    }
}

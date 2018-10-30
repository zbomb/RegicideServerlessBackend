﻿using System;
using System.Collections.Generic;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

using Amazon.Lambda.Core;

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
        public string Token { get; set; }
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


    public static class RegDatabase
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

        public enum LoginResult
        {
            Error = 0,
            Invalid = 1,
            Success = 2
        }

        public static LoginResult PerformLogin( string Username, string PassHash, string inTable, string inToken, out Account Info )
        {
            if( DatabaseClient == null )
                Init();

            Info = null;

            // Update the token and retrieve the basic account info in a single call to reducde api calls
            var UpReq = new UpdateItemRequest()
            {
                TableName = inTable,
                ReturnValues = ReturnValue.ALL_NEW,
                Key =
                {
                    { "User", new AttributeValue { S = Username } },
                    { "Property", new AttributeValue { N = ( (int) AccountEntry.BasicInfo ).ToString() } }
                },
                UpdateExpression = "SET #t = :in_token",
                ConditionExpression = "#pwd = :in_pwd",
                ExpressionAttributeNames =
                {
                    { "#t", "Token" },
                    { "#pwd", "PassHash" }
                },
                ExpressionAttributeValues =
                {
                    { ":in_token", new AttributeValue { S = inToken } },
                    { ":in_pwd", new AttributeValue { S = PassHash } }
                },
            };

            var UpdTask = DatabaseClient.UpdateItemAsync( UpReq );
            UpdTask.Wait();

            var UpdResult = UpdTask.Result;

            if( UpdResult?.Attributes == null || UpdResult.Attributes.Count == 0 )
                return LoginResult.Invalid;

            // Double check password hash
            if( !UpdResult.Attributes.ContainsKey( "PassHash" ) || !UpdResult.Attributes[ "PassHash" ].S.Equals( PassHash ) )
                return LoginResult.Invalid;

            // Okay, we logged in properly, now we need to query all of the account info for this user and serialize it
            var TotalQuery = new QueryRequest()
            {
                TableName = inTable,
                ConsistentRead = true,
                KeyConditionExpression = "#u = :in_user and #pr != :basic_prop",
                ExpressionAttributeNames =
                {
                    { "#u", "User" },
                    { "#pr", "Property" }
                },
                ExpressionAttributeValues =
                {
                    { ":in_user", new AttributeValue { S = Username.ToLower() } },
                    { ":basic_prop", new AttributeValue { N = ( (int) AccountEntry.BasicInfo ).ToString() } }
                }
            };

            var TotalTask = DatabaseClient.QueryAsync( TotalQuery );
            TotalTask.Wait();

            var FullAccount = TotalTask.Result;

            if( FullAccount?.Items == null || FullAccount.Count == 0 )
            {
                LambdaLogger.Log( String.Format( "[ERROR] Account '{0}' logged in successfully.. but the full account couldnt be queried!", Username ) );
                return LoginResult.Error;
            }

            var LoadedAccount = new Account
            {
                Info = ProcessAccountInfo( UpdResult.Attributes, Username ),
                Cards = new List<Card>(),
                Decks = new List<Deck>(),
                Achievements = new List<Achievement>()
            };

            if( LoadedAccount.Info == null )
            {
                LambdaLogger.Log( String.Format( "[ERROR] Account '{0}' logged in successfully.. but the basic account info couldnt be parsed!", Username ) );
                return LoginResult.Error;
            }

            foreach( var Property in FullAccount.Items )
            {
                if( Property[ "Property" ].N == ( (int) AccountEntry.CardsLower ).ToString() ||
                  Property[ "Property" ].N == ( (int) AccountEntry.CardsUpper ).ToString() )
                {
                    var Cards = ProcessCardList( Property, Username );
                    if( Cards != null )
                        LoadedAccount.Cards.AddRange( Cards );
                }
                else if( Property[ "Property" ].N == ( (int) AccountEntry.Achievements ).ToString() )
                {
                    var Achvs = ProcessAchievementList( Property, Username );
                    if( Achvs != null )
                        LoadedAccount.Achievements = Achvs;
                }
                else if( Int32.Parse( Property[ "Property" ].N ) > 4 )
                {
                    var NewDeck = ProcessDeck( Property, Username );
                    if( NewDeck != null )
                        LoadedAccount.Decks.Add( NewDeck );
                }
            }

            // TODO: Re-evalute this, should we check if cards were loaded? What if a user doesnt have cards? Should we not allow users to have no cards?
            if( LoadedAccount.Cards.Count == 0 )
            {
                LambdaLogger.Log( String.Format( "[ERROR] Account '{0}' logged in.. but the card list couldnt be loaded!", Username ) );
                return LoginResult.Error;
            }

            Info = LoadedAccount;
            return LoginResult.Success;
        }



        /*==========================================================================================
         *      Internal Helpers For Processing Accounts
         *  - Each part of an account (basic info, cards, decks, achievements) is seperate
         *  - To query for a single part of this info, you can use QueryProperty
         *  - To query for multiple parts of this info, use QueryPropertyList
         *  - These functions define how each part of the account is read from the database
         *    and they are called as part of the two functions mention previously
        ==========================================================================================*/
        static BasicAccountInfo ProcessAccountInfo( Dictionary<string, AttributeValue> Result, string Username )
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
            if( String.IsNullOrWhiteSpace( Result[ "Token" ].S ) )
            {
                Console.WriteLine( "[RegAPI] Warning! Account '{0}' has invalid token set in database! This API call failed!", Username.ToLower() );
                return null;
            }

            // Now that everything we have is validated, build the Basic Info object
            var Info = new BasicAccountInfo()
            {
                Coins = Coins,
                Email = Result[ "Email" ].S,
                DisplayName = Result[ "DispName" ].S,
                Username = Result[ "User" ].S,
                Token = Result[ "Token" ].S
            };

            Info.Verified = Result.ContainsKey( "Verify" ) && Result[ "Verify" ].BOOL;

            return Info;
        }


        public static List<Card> ProcessCardList( Dictionary<string, AttributeValue> Result, string Username )
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


        public static Deck ProcessDeck( Dictionary<string, AttributeValue> Result, string Username )
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


        public static List<Achievement> ProcessAchievementList( Dictionary<string, AttributeValue> Result, string Username )
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


    }
}

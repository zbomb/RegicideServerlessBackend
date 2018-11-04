using System;
using System.Collections.Generic;

using Amazon.DynamoDBv2.Model;


namespace Regicide.API
{

    public static partial class Serializer
    {

        public static Dictionary< string, AttributeValue > SerializeBasicInfo( BasicAccountInfo Input, string PassHash, string Token, bool bIgnoreCoins = false )
        {
            if( Input == null || String.IsNullOrEmpty( Input.Username.ToLower() ) )
                return null;

            var Output = new Dictionary<string, AttributeValue>
                {
                    { "User", new AttributeValue{ S = Input.Username.ToLower() } },
                    { "Property", new AttributeValue{ N = ( (int) AccountEntry.BasicInfo ).ToString() } }
                };

            if( !String.IsNullOrEmpty( Input.Email ) )
            {
                // We have to store two copies of email, one is the original case the user entered, and the other is a lowercase copy, used for indexing
                Output.Add( "Email", new AttributeValue { S = Input.Email.ToLower() } );
                Output.Add( "OrigEmail", new AttributeValue { S = Input.Email } );
            }
            if( !String.IsNullOrEmpty( Input.DisplayName ) )
                Output.Add( "DispName", new AttributeValue { S = Input.DisplayName } );
            if( !String.IsNullOrEmpty( PassHash ) )
                Output.Add( "PassHash", new AttributeValue { S = PassHash } );
            if( !String.IsNullOrEmpty( Token ) )
                Output.Add( "Token", new AttributeValue { S = Token } );
            if( !bIgnoreCoins )
                Output.Add( "Coins", new AttributeValue { N = Input.Coins.ToString() } );

            return Output;
        }


        public static bool SerializeCards( List< Card > Input, string inUser, out Dictionary< string, AttributeValue > LowerSet, out Dictionary< string, AttributeValue > UpperSet )
        {
            LowerSet = null;
            UpperSet = null;
            inUser = inUser?.ToLower();

            if( Input == null || String.IsNullOrEmpty( inUser ) )
                return false;
                
            foreach( var C in Input )
            {
                if( C.Id > Constants.MaxLowerCardId )
                {
                    if( UpperSet == null )
                    {
                        UpperSet = new Dictionary<string, AttributeValue> 
                        { 
                            { "User", new AttributeValue { S = inUser } }, 
                            { "Property", new AttributeValue { N = ( (int) AccountEntry.CardsUpper ).ToString() } },
                            { "Cards", new AttributeValue { M = new Dictionary<string, AttributeValue>() } }
                        };
                    }
                    // Check for duplicates
                    if( UpperSet[ "Cards" ].M.ContainsKey( C.Id.ToString() ) )
                    {
                        Console.WriteLine( "(API) [WARNING] Serialization: Duplicate card '{0}' found in account '{1}' card list", C.Id, inUser );
                        continue;
                    }

                    UpperSet[ "Cards" ].M.Add( C.Id.ToString(), new AttributeValue { N = C.Ct.ToString() } );
                }
                else
                {
                    if( LowerSet == null )
                    {
                        LowerSet = new Dictionary<string, AttributeValue> 
                        { 
                            { "User", new AttributeValue { S = inUser } }, 
                            { "Property", new AttributeValue { N = ( (int) AccountEntry.CardsLower ).ToString() } },
                            { "Cards", new AttributeValue { M = new Dictionary<string, AttributeValue>() } }
                        };
                    }
                    // Check for duplicates
                    if( LowerSet[ "Cards" ].M.ContainsKey( C.Id.ToString() ) )
                    {
                        Console.WriteLine( "(API) [WARNING] Serialization: Duplicate card '{0}' found in account '{1}' card list", C.Id, inUser );
                        continue;
                    }

                    LowerSet[ "Cards" ].M.Add( C.Id.ToString(), new AttributeValue { N = C.Ct.ToString() } );
                }
            }

            return true;
        }


        public static List< Dictionary< string, AttributeValue > > SerializeDecks( List< Deck > Input, string inUser )
        {
            inUser = inUser?.ToLower();

            if( Input == null || String.IsNullOrEmpty( inUser ) )
                return null;

            var Output = new List<Dictionary<string, AttributeValue>>();
            foreach( var D in Input )
            {
                // Basic Validation thats needed to ensure proper storage of this deck
                // Full validation is done on creation
                if( D.Id == 0 )
                {
                    Console.WriteLine( "(API) [WARNING] Serializtion: Deck identifier '0' being used by '{0}'.. ignoring deck.", inUser );
                    continue;
                }
                if( String.IsNullOrWhiteSpace( D.Name ) )
                {
                    Console.WriteLine( "(API) [WARNING] Serialization: Deck '{0}' owned by '{1}' has an invalid name!", D.Id, inUser );
                    continue;
                }
                if( Output.Exists( X => ( Int32.Parse( X[ "Property" ].N ) - 4 ) == D.Id ) )
                {
                    Console.WriteLine( "(API) [WARNING] Serialization: Deck '{0}' owned by '{1}' has duplicate identifier!", D.Id, inUser );
                    continue;
                }

                var SerDeck = new Dictionary<string, AttributeValue>
                {
                    { "User", new AttributeValue { S = inUser } },
                    { "Property", new AttributeValue { N = ( D.Id + 4 ).ToString() } },
                    { "DispName", new AttributeValue { S = D.Name } },
                    { "Cards", new AttributeValue { M = new Dictionary<string, AttributeValue>() } }
                };

                foreach( var C in D.Cards )
                {
                    if( C.Ct == 0 )
                        continue;

                    if( SerDeck[ "Cards" ].M.ContainsKey( C.Id.ToString() ) )
                    {
                        Console.WriteLine( "(API) [WARNING] Serialization: Duplicate card '{0}' found in deck '{1}' owned by '{2}'", C.Id, D.Id, inUser );
                    }

                    SerDeck[ "Cards" ].M[ C.Id.ToString() ] = new AttributeValue { N = C.Ct.ToString() };

                }

                Output.Add( SerDeck );
            }

            return Output;
        }


        public static Dictionary< string, AttributeValue > SerializeAchievements( List< Achievement > Input, string inUser )
        {
            inUser = inUser?.ToLower();

            if( Input == null || String.IsNullOrEmpty( inUser ) )
                return null;

            var Output = new Dictionary<string, AttributeValue>
            {
                { "User", new AttributeValue { S = inUser } },
                { "Property", new AttributeValue { N = ( (int) AccountEntry.Achievements ).ToString() } },
                { "Achv", new AttributeValue { L = new List<AttributeValue>() } }
            };

            foreach( var Achv in Input )
            {
                if( Output[ "Achv" ].L.Exists( X => X.M[ "id" ].N == Achv.Id.ToString() ) )
                {
                    Console.WriteLine( "(API) [WARNING] Serialization: Duplicate achievement '{0}' found in '{1}'s account.", Achv.Id, inUser );
                    continue;
                }

                Output[ "Achv" ].L.Add( new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                        {
                            { "id", new AttributeValue { N = Achv.Id.ToString() } },
                            { "cp", new AttributeValue { BOOL = Achv.Complete } },
                            { "st", new AttributeValue { N = Achv.State.ToString() } }
                        }
                } );
            }

            return Output;
        }

    }
}
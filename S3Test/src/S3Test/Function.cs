using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2;

using Amazon.S3.Model;
using Amazon.S3;

using Regicide.API;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace S3Test
{
    public class TestRequest
    {
        public int BullShit { get; set; }
    }

    public class TestResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class Function
    {

        public TestResponse FunctionHandler( TestRequest Params, ILambdaContext context )
        {
            var Output = new TestResponse()
            {
                Success = true,
                Error = String.Empty
            };

            // Were going to create an account with the largest possible card list to check if 
            // it causes an error or not
            Account newAccount = new Account()
            {
                Info = new BasicAccountInfo()
                {
                    Username = "zberry7",
                    DisplayName = "Zack Berry",
                    Email = "zberry1@outlook.com",
                    Coins = 150,
                    Verified = true
                },
                Cards = new List<Card>(),
                Decks = new List<Deck>(),
                Achievements = new List<Achievement>()
            };

            Random Rand = new Random();

            for( ushort i = 0; i < UInt16.MaxValue / 2; i++ )
            {
                newAccount.Cards.Add( new Card()
                {
                    Identifier = i,
                    Count = UInt16.MaxValue
                } );
            }

            // Were going to leave the decks and achievements empty to see if it causes an error
            // Now, we have to serialize to the database
            using( var Database = new AmazonDynamoDBClient() )
            {
                // Basic Info
                var SerializedAccount = new Dictionary<string, AttributeValue>()
                {
                    { "User", new AttributeValue { S = newAccount.Info.Username.ToLower() } },
                    { "Property", new AttributeValue { N = "0" } },
                    { "Email", new AttributeValue { S = newAccount.Info.Email } },
                    { "DispName", new AttributeValue { S = newAccount.Info.DisplayName } },
                    { "Coins", new AttributeValue { N = newAccount.Info.Coins.ToString() } },
                    { "Verify", new AttributeValue { BOOL = newAccount.Info.Verified } }
                };

                var InfoPut = new PutRequest( SerializedAccount );

                // Card List
                var SerializedCards = new Dictionary<string, AttributeValue>()
                {
                    { "User", new AttributeValue { S = newAccount.Info.Username.ToLower() } },
                    { "Property", new AttributeValue { N = "1" } },
                    { "Cards", new AttributeValue { M = new Dictionary<string, AttributeValue>() } }
                };

                foreach( var c in newAccount.Cards )
                {
                    SerializedCards[ "Cards" ].M.Add( c.Identifier.ToString(), new AttributeValue { N = c.Count.ToString() } );
                }

                var CardPut = new PutRequest( SerializedCards );

                // Empty Achievement List
                var SerializedAchv = new Dictionary<string, AttributeValue>
                {
                    { "User", new AttributeValue { S = newAccount.Info.Username.ToLower() } },
                    { "Property", new AttributeValue { N = "3" } }
                };

                var AchvPut = new PutRequest( SerializedAchv );

                var BatchWriteReq = new BatchWriteItemRequest();
                BatchWriteReq.RequestItems.Add( "Accounts", new List<WriteRequest>
                {
                    new WriteRequest( InfoPut ),
                    new WriteRequest( CardPut ),
                    new WriteRequest( AchvPut )
                } );

                var WriteTask = Database.BatchWriteItemAsync( BatchWriteReq );
                WriteTask.Wait();
            }
            return Output;
        }

        /*
        TestResponse TestDynamo( TestRequest Params, List< Account > Accounts )
        {
            var Output = new TestResponse()
            {
                ErrMessage = String.Empty
            };

            // TODO: Create table called AccountProps and Decks
            // AccountProps uses the username as hash key, and proptype as sort key

            using( var Client = new AmazonDynamoDBClient() )
            {
                // Perform inserts
                double AverageInsTime = 0.0d;

                try
                {
                    Table ActProps = Table.LoadTable( Client, "Accounts" );

                    foreach( var Act in Accounts )
                    {
                        DateTime InsStart = DateTime.UtcNow;

                        // Create Main Account Info
                        Document NewAccount = new Document
                        {
                            { "Username", Act.User },
                            { "Type", (int) PropType.BaseInfo },
                            { "Email", Act.Email },
                            { "PassHash", Act.PassHash },
                            { "Id", Act.Id },
                            { "DispName", Act.DispName },
                            { "Coins", 100 }
                        };

                        var BasePut = ActProps.PutItemAsync( NewAccount );

                        // Create Card List
                        Document CardList = new Document();
                        foreach( var c in Act.Cards )
                        {
                            // If this card is already in the list, update the count (should never happen)
                            if( CardList.Any( X => X.Key == c.Id.ToString() ) )
                            {
                                CardList[ c.Id.ToString() ] = c.Ct;
                                continue;
                            }

                            CardList.Add( c.Id.ToString(), c.Ct );
                        }

                        Document CardsInfo = new Document
                        {
                            { "Username", Act.User },
                            { "Type", (int) PropType.CardListA },
                            { "Cards", CardList }
                        };

                        var CardPut = ActProps.PutItemAsync( CardsInfo );

                        // Create Achievement List
                        List<Document> Achvs = new List<Document>();
                        for( int i = 0; i < 30;  i++ )
                        {
                            Achvs.Add( new Document
                            {
                                { "id", i },
                                { "pr", 0.5f },
                                { "cp", true },
                                { "st", 10 }
                            } );
                        }

                        Document AchvInfo = new Document
                        {
                            { "Username", Act.User },
                            { "Type", (int) PropType.Achv },
                            { "Achv", Achvs }
                        };

                        var AchvPut = ActProps.PutItemAsync( AchvInfo );

                        // Now for decks, we will insert into the same table, just use the rest of the prop numbers up until 36
                        List<Task> FinalTasks = new List<Task>();
                        foreach( var d in Act.Decks )
                        {
                            Document DeckCards = new Document();
                            foreach( var c in d.Cards )
                            {
                                if( DeckCards.Any( X => X.Key == c.Id.ToString() ) )
                                {
                                    // If this card is already in the deck, then update count 
                                    DeckCards[ c.Id.ToString() ] = c.Ct;
                                    continue;
                                }

                                DeckCards.Add( c.Id.ToString(), c.Ct );
                            }

                            Document NewDeck = new Document
                            {
                                { "Username", Act.User },
                                { "Type", 5 + d.Id },
                                { "DispName", d.Name },
                                { "Cards", DeckCards }
                            };

                            FinalTasks.Add( ActProps.PutItemAsync( NewDeck ) );
                        }

                        FinalTasks.Add( BasePut );
                        FinalTasks.Add( CardPut );
                        FinalTasks.Add( AchvPut );

                        // Now that all necisary put tasks have been created, lets wait until we complete all of them
                        Task.WaitAll( FinalTasks.ToArray() );
                        TimeSpan InsTime = DateTime.UtcNow - InsStart;
                        AverageInsTime += InsTime.TotalMilliseconds / Accounts.Count;
                    }


                    Output.InsTime = AverageInsTime;
                }
                catch( Exception Ex )
                {
                    Output.Error = true;
                    Output.ErrMessage = "[INSERT] " + Ex;

                    return Output;
                }


                double AverageQueryTime = 0.0d;

                try
                {
                    foreach( var Act in Accounts )
                    {
                        DateTime QueryStart = DateTime.UtcNow;

                        Account ReadAccount = new Account()
                        {
                            Cards = new List<Card>(),
                            Decks = new List<Deck>(),
                            Achvs = new List<Achievement>()
                        };

                        // First, lets read everything from the table related to this player in a single query
                        var Request = new QueryRequest()
                        {
                            TableName = "Accounts",
                            KeyConditionExpression = "Username = :in_user",
                            ConsistentRead = true,
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                { ":in_user", new AttributeValue{ S = Act.User } }
                            }
                        };

                        var QueryTask = Client.QueryAsync( Request );
                        QueryTask.Wait();

                        var Response = QueryTask.Result;

                        if( Response == null )
                        {
                            Output.Error = true;
                            Output.ErrMessage += String.Format( "[Query] Failed to lookup user '{0}'", Act.User );
                            continue;
                        }

                        // Go through results and rebuild account structure
                        foreach( var Property in Response.Items )
                        {
                            if( Property[ "Type" ].N == ( (int) PropType.BaseInfo ).ToString() )
                            {
                                ReadAccount.User = Property[ "Username" ].S;
                                ReadAccount.Id = UInt32.Parse( Property[ "Id" ].N );
                                ReadAccount.PassHash = Property[ "PassHash" ].S;
                                ReadAccount.Email = Property[ "Email" ].S;
                                ReadAccount.DispName = Property[ "DispName" ].S;
                            }
                            else if( Property[ "Type" ].N == ( (int) PropType.CardListA ).ToString() ||
                                    Property[ "Type" ].N == ( (int) PropType.CardListB ).ToString() )
                            {
                                foreach( var c in Property[ "Cards" ].M )
                                {
                                    // Key = Id, Value = Count
                                    ReadAccount.Cards.Add( new Card()
                                    {
                                        Id = UInt16.Parse( c.Key ),
                                        Ct = UInt16.Parse( c.Value.N )
                                    } );
                                }
                            }
                            else if( Property[ "Type" ].N == ( (int) PropType.Achv ).ToString() )
                            {
                                foreach( var a in Property[ "Achv" ].L )
                                {
                                    // If this achv is already loaded, then skip
                                    if( ReadAccount.Achvs.Any( X => X.Id == UInt32.Parse( a.M[ "id" ].N ) ) )
                                    {
                                        continue;
                                    }

                                    ReadAccount.Achvs.Add( new Achievement()
                                    {
                                        Id = UInt32.Parse( a.M[ "id" ].N ),
                                        Progress = float.Parse( a.M[ "pr" ].N ),
                                        Complete = a.M[ "cp" ].BOOL,
                                        State = Int32.Parse( a.M[ "st" ].N )
                                    } );
                                }
                            }
                            else if( Int32.Parse( Property[ "Type" ].N ) > (int) PropType.Achv )
                            {
                                // Decks
                                if( ReadAccount.Decks.Any( X => X.Id == UInt32.Parse( Property[ "Type" ].N ) - 5 ) )
                                {
                                    continue;
                                }

                                Deck newDeck = new Deck
                                {
                                    Id = UInt32.Parse( Property[ "Type" ].N ) - 5,
                                    Name = Property[ "DispName" ].S,
                                    Cards = new List<Card>()
                                };

                                foreach( var c in Property[ "Cards" ].M )
                                {
                                    newDeck.Cards.Add( new Card()
                                    {
                                        Id = UInt16.Parse( c.Key ),
                                        Ct = UInt16.Parse( c.Value.N )
                                    } );
                                }

                                ReadAccount.Decks.Add( newDeck );
                            }

                            TimeSpan QueryTime = DateTime.UtcNow - QueryStart;
                            AverageQueryTime += QueryTime.TotalMilliseconds / Accounts.Count;

                        }
                    }

                    Output.RetTime = AverageQueryTime;
                }
                catch( Exception Ex )
                {
                    Output.Error = true;
                    Output.ErrMessage = "[QUERY] " + Ex;

                    return Output;
                }
            }


            return Output;
        }

        TestResponse TestDynamoWithS3( TestRequest Params, List< Account > Accounts )
        {
            var Output = new TestResponse()
            {
                ErrMessage = String.Empty
            };

            using( var S3 = new AmazonS3Client() )
            {
                using( var Dynamo = new AmazonDynamoDBClient() )
                {
                    using( var Context = new DynamoDBContext( Dynamo ) )
                    {

                        double AverageTime = 0.0d;

                        try
                        {
                            // Put all accounts in S3
                            foreach( var act in Accounts )
                            {
                                DateTime InsStart = DateTime.UtcNow;

                                // Serialize account
                                var Serializer = new Amazon.Lambda.Serialization.Json.JsonSerializer();
                                string SerAct = null;

                                // Were going to build a struture that takes up much less space once serialized
                                AccountFile newFile = new AccountFile()
                                {
                                    Id = act.Id,
                                    Cards = new Dictionary<int, int>(),
                                    Decks = new List<S3Deck>()
                                };

                                foreach( var c in act.Cards )
                                    newFile.Cards.Add( c.Id, c.Ct );

                                foreach( var d in act.Decks )
                                {
                                    var newDeck = new S3Deck
                                    {
                                        Id = d.Id,
                                        Name = d.Name,
                                        Cards = new Dictionary<int, int>()
                                    };

                                    foreach( var c in d.Cards )
                                        newDeck.Cards.Add( c.Id, c.Ct );

                                    newFile.Decks.Add( newDeck );
                                }

                                using( var Stream = new System.IO.MemoryStream() )
                                {
                                    Serializer.Serialize( newFile, Stream );
                                    SerAct = System.Text.Encoding.UTF8.GetString( Stream.ToArray() );
                                }

                                var PutReq = new PutObjectRequest()
                                {
                                    BucketName = "regicidetesting",
                                    Key = act.S3Link,
                                    ContentBody = SerAct
                                };

                                PutReq.Metadata.Add( "reg-userid", newFile.Id.ToString() );

                                var PutTask = S3.PutObjectAsync( PutReq );
                                
                                // Now we can create the player account entry in DynamoDB
                                var SaveTask = Context.SaveAsync( act );

                                Task.WaitAll( PutTask, SaveTask );

                                TimeSpan InsTime = DateTime.UtcNow - InsStart;
                                AverageTime += InsTime.TotalMilliseconds / Accounts.Count;
                            }
                        }
                        catch( Exception Ex )
                        {
                            Output.Error = true;
                            Output.ErrMessage = "[INSERT] " + Ex;

                            return Output;
                        }

                        Output.InsTime = AverageTime;

                        // Now, we need to loop through and query the accounts
                        double AverageQueryTime = 0.0d;

                        try
                        {
                            foreach( var Act in Accounts )
                            {
                                DateTime QueryStart = DateTime.UtcNow;

                                var LoadTask = Context.LoadAsync<Account>( Act.User );

                                var Result = LoadTask.Result;
                                if( Result == null || String.IsNullOrWhiteSpace( Result.S3Link ) )
                                {
                                    LambdaLogger.Log( "[RegTest] Failed to query an account!" );
                                    continue;
                                }

                                GetObjectRequest Request = new GetObjectRequest()
                                {
                                    BucketName = "regicidetesting",
                                    Key = Result.S3Link
                                };

                                var GetTask = S3.GetObjectAsync( Request );

                                // Quey in parallel
                                Task.WaitAll( LoadTask, GetTask );

                                TimeSpan QueryTime = DateTime.UtcNow - QueryStart;
                                AverageQueryTime += QueryTime.TotalMilliseconds / Accounts.Count;
                            }

                            Output.RetTime = AverageQueryTime;
                        }
                        catch( Exception Ex )
                        {
                            Output.Error = true;
                            Output.ErrMessage = "[QUERY] " + Ex;

                            return Output;
                        }
                    }
                }
            }

            return Output;
        }
        */
    }
}

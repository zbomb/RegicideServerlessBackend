using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using Amazon.Lambda.Core;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

using MySql.Data.MySqlClient;

using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace DatabaseTests
{
    public struct TestRequest
    {
        public int CardsPerClient { get; set; }
        public int ClientCount { get; set; }
        public int QueryCount { get; set; }
    }

    public enum TestStatus
    {
        OtherError = 0,
        SqlError = 1,
        DynError = 2,
        BothError = 3,
        Success = 4
    }

    public struct TestResults
    {
        public TestStatus State { get; set; }

        public double InsTimeSql { get; set; }
        public double InsTimeDyn { get; set; }

        public double QueryTimeSql { get; set; }
        public double QueryTimeDyn { get; set; }

        public string ErrorMessage { get; set; }
    }

    public class Card
    {
        public ushort Id { get; set; }
        public ushort Ct { get; set; }
        public uint Pl { get; set; }
    }


    public class Function
    {
        static string ConnectionString;

        public Function()
        {
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

        static void WaitForTable( AmazonDynamoDBClient Client, string inTableName, TableStatus DesiredState, int TimeoutMilliseconds )
        {
            DateTime Timeout = DateTime.UtcNow + new TimeSpan( 0, 0, 0, 0, TimeoutMilliseconds );

            while( DateTime.UtcNow < Timeout )
            {
                Thread.Sleep( 200 );
                
                try
                {
                    var DescribeRequest = Client.DescribeTableAsync( new DescribeTableRequest( inTableName ) );
                    DescribeRequest.Wait();

                    var Result = DescribeRequest.Result;

                    if( Result.Table == null && DesiredState == null )
                        return;

                    if( Result.Table != null && Result.Table.TableStatus == DesiredState )
                        return;

                }
                catch( ResourceNotFoundException  )
                {
                    if( DesiredState == null )
                        return;
                }
                catch( AggregateException )
                {
                    if( DesiredState == null )
                        return;
                }
            }

            throw new TimeoutException();
        }


        // Function Entry
        public TestResults FunctionHandler( TestRequest Params, ILambdaContext Context )
        {
            TestResults Results = new TestResults()
            {
                State = TestStatus.Success,
                ErrorMessage = String.Empty
            };

            List<Card> Payload = new List<Card>();

            var Rand = new Random();

            // Generate payload for testing, so its the same for both tests
            for( int User = 0; User < Params.ClientCount; User++ )
            {
                uint PlayerId = (uint) Rand.Next();

                for( int i = 0; i < Params.CardsPerClient; i++ )
                {
                    // Generate a randomized card info
                    Payload.Add( new Card()
                    {
                        Id = (ushort) Rand.Next( 1, UInt16.MaxValue ),
                        Ct = (ushort) Rand.Next( 1, 30 ),
                        Pl = i % 2 == 0 ? PlayerId : (uint) Rand.Next() // We want it to be partially grouped by user id, but not completley
                    } );
                }
            }

            // Build a list of card id's to query from the database
            List<Card> QueryCards = new List<Card>();

            for( int i = 0; i < Params.QueryCount; i++ )
            {
                var RandCard = Payload[ Rand.Next( 0, Payload.Count ) ];
                QueryCards.Add( RandCard );
            }

            // First, lets query dynamo db performance
            using( AmazonDynamoDBClient DynClient = new AmazonDynamoDBClient() )
            {

                if( Table.TryLoadTable( DynClient, "TestTable", out Table testTable ) )
                {
                    if( testTable != null )
                    {
                        try
                        {
                            // If the table already exists, then we need to delete it and recreate it to 
                            // ensure its empty
                            var Request = DynClient.DeleteTableAsync( new DeleteTableRequest( "TestTable" ) );
                            Request.Wait();

                            // Use null to wait for deletion
                            WaitForTable( DynClient, "TestTable", null, 80000 );
                        }
                        catch( Exception )
                        {
                            LambdaLogger.Log( "[RegTest] Timeout while deleting old DynamoDB test table! Continuing regardless.." );
                        }
                        
                    }
                }

                var CreateResult = DynClient.CreateTableAsync( new CreateTableRequest()
                {
                    TableName = "TestTable",
                    KeySchema = new List<KeySchemaElement>()
                    {
                        new KeySchemaElement( "id", KeyType.HASH ),
                        new KeySchemaElement( "pl", KeyType.RANGE )
                    },
                    AttributeDefinitions = new List<AttributeDefinition>()
                    {
                        new AttributeDefinition( "id", ScalarAttributeType.N ),
                        new AttributeDefinition( "pl", ScalarAttributeType.N )
                    },
                    ProvisionedThroughput = new ProvisionedThroughput( 5, 5 )
                } );
                CreateResult.Wait();

                try
                {
                    WaitForTable( DynClient, "TestTable", TableStatus.ACTIVE, 80000 );
                }
                catch( TimeoutException )
                {
                    LambdaLogger.Log( "[RegTest] Timeout while creating DynamoDB test table! Continuing regardless.." );
                }

                // Load the clean table
                var TestTable = Table.LoadTable( DynClient, "TestTable" );

                // Log function start
                LambdaLogger.Log( "[RegTest] Begin DynammoDB Inserts..." );
                DateTime InsStart = DateTime.UtcNow;

                try
                {
                    foreach( var Item in Payload )
                    {
                        Document newDoc = new Document
                        {
                            { "id", Item.Id },
                            { "ct", Item.Ct },
                            { "pl", Item.Pl }
                        };

                        var putTask = TestTable.PutItemAsync( newDoc );
                        putTask.Wait();
                    }

                    // Calculate execution time
                    TimeSpan InsTime = DateTime.UtcNow - InsStart;
                    Results.InsTimeDyn = InsTime.TotalMilliseconds;
                    LambdaLogger.Log( String.Format( "[RegTest] DynammoDB inserts complete! Execution Time: {0}ms", InsTime.TotalMilliseconds ) );
                }
                catch( Exception Ex )
                {
                    // On insert error
                    LambdaLogger.Log( "[RegTest] An exception was thrown while running DynamoDB insert tests!" );
                    LambdaLogger.Log( Ex.ToString() );

                    Results.InsTimeDyn = 0.0d;
                    Results.State = TestStatus.DynError;
                    Results.ErrorMessage += Ex + "\n";
                }

                // Log Query Start
                LambdaLogger.Log( "[RegTest] Begining DynammoDB queries..." );
                DateTime QryStart = DateTime.UtcNow;

                try
                {
                    for( int i = 0; i < QueryCards.Count; i++ )
                    {
                        // Query database for this card id
                        var query = TestTable.GetItemAsync( QueryCards[ i ].Id, QueryCards[ i ].Pl );
                        query.Wait();
                    }

                    // Calculate execution time
                    TimeSpan QryTime = DateTime.UtcNow - QryStart;
                    Results.QueryTimeDyn = QryTime.TotalMilliseconds;
                    LambdaLogger.Log( String.Format( "[RegTest] DynamoDB query test complete! Execution time: {0}ms", QryTime.TotalMilliseconds ) );
                }
                catch( Exception Ex )
                {
                    // On query error
                    LambdaLogger.Log( "[RegTest] An exception was thrown while running DynamoDB query tests!" );
                    LambdaLogger.Log( Ex.ToString() );

                    Results.QueryTimeDyn = 0.0d;
                    Results.State = TestStatus.DynError;
                    Results.ErrorMessage += Ex + "\n";
                }

                // Cleanup DynamoDB Table
                // Quicker to just delete and remake the table
                var DeleteResult = DynClient.DeleteTableAsync( new DeleteTableRequest()
                {
                    TableName = "TestTable"
                } );
                DeleteResult.Wait();
            }


            // Begin MySql testing
            using( var Database = new MySqlConnection( ConnectionString ) )
            {
                try
                {
                    Database.Open();
                }
                catch( Exception Ex )
                {
                    LambdaLogger.Log( String.Format( "[RegTest] Failed to connect to MySql database! {0}", Ex.Message ) );
                    Results.State = Results.State == TestStatus.DynError ? TestStatus.BothError : TestStatus.SqlError;
                    Results.InsTimeSql = 0.0d;
                    Results.QueryTimeSql = 0.0d;
                    Results.ErrorMessage += Ex + "\n";

                    return Results;
                }

                // Create table if needed
                using( var Command = new MySqlCommand( "CREATE TABLE IF NOT EXISTS `TestTable` (" +
                                     "`id` smallint( 5 ) unsigned NOT NULL," +
                                     "`ct` smallint( 3 ) unsigned DEFAULT NULL," +
                                     "`pl` int( 10 ) unsigned NOT NULL," +
                                     "PRIMARY KEY(`id`,`pl`)" +
                                     ") ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;", Database ) )
                {
                    try
                    {
                        Command.ExecuteNonQuery();
                    }
                    catch( Exception Ex )
                    {
                        if( Ex is MySqlException MyEx )
                        {
                            if( MyEx.Code != (uint) MySqlErrorCode.TableExists )
                            {
                                LambdaLogger.Log( "[RegTest] An exception was thrown while creating MySql testing table!" );
                                LambdaLogger.Log( MyEx.ToString() );

                                Results.ErrorMessage += MyEx + "\n";
                                Results.State = TestStatus.SqlError;
                                return Results;
                            }
                        }
                        else
                        {
                            LambdaLogger.Log( "[RegTest] A Non-MySql exception was thrown while creating MySql testing table!" );
                            LambdaLogger.Log( Ex.ToString() );

                            Results.ErrorMessage += Ex + "\n";
                            Results.State = TestStatus.OtherError;
                            return Results;
                        }
                    }
                }

                // Begin Sql Insert Testing
                LambdaLogger.Log( "[RegTest] Begining SQL insert test..." );
                DateTime InsStart = DateTime.UtcNow;

                try
                {
                    foreach( var Card in Payload )
                    {
                        using( var Command = new MySqlCommand( "INSERT INTO TestTable (id, ct, pl) VALUES( @inid, @inct, @inpl )", Database ) )
                        {
                            Command.Parameters.AddWithValue( "@inid", Card.Id );
                            Command.Parameters.AddWithValue( "@inct", Card.Ct );
                            Command.Parameters.AddWithValue( "@inpl", Card.Pl );

                            Command.ExecuteNonQuery();
                        }
                    }

                    TimeSpan InsTime = DateTime.UtcNow - InsStart;
                    Results.InsTimeSql = InsTime.TotalMilliseconds;
                    LambdaLogger.Log( String.Format( "[RegTest] MySql Insert test complete! Execution time: {0}ms", InsTime.TotalMilliseconds ) );
                }
                catch( Exception Ex )
                {
                    LambdaLogger.Log( "[RegTest] An exception was thrown while running SQL insert test!" );
                    LambdaLogger.Log( Ex.ToString() );

                    Results.InsTimeSql = 0.0d;
                    Results.State = Results.State == TestStatus.DynError ? TestStatus.BothError : TestStatus.SqlError;
                    Results.ErrorMessage += Ex + "\n";
                }

                LambdaLogger.Log( "[RegTest] Begining SQL query test..." );
                DateTime QryStart = DateTime.UtcNow;

                try
                {
                    foreach( var Card in QueryCards )
                    {
                        using( var Command = new MySqlCommand( "SELECT * FROM TestTable WHERE id=@inid AND pl=@inpl LIMIT 1", Database ) )
                        {
                            Command.Parameters.AddWithValue( "@inid", Card.Id );
                            Command.Parameters.AddWithValue( "@inpl", Card.Pl );

                            using( var Reader = Command.ExecuteReader() )
                            {
                                if( Reader.HasRows && Reader.Read() )
                                {
                                    Reader.GetUInt16( "id" );
                                    Reader.GetUInt32( "pl" );
                                }
                                else
                                    throw new Exception( "Failed to read item that was inserted in previous test" );
                            }
                        }
                    }

                    TimeSpan QryTime = DateTime.UtcNow - QryStart;
                    Results.QueryTimeSql = QryTime.TotalMilliseconds;
                    LambdaLogger.Log( String.Format( "[RegTest] MySql Query test complete! Execution time: {0}ms", QryTime.TotalMilliseconds ) );
                }
                catch( Exception Ex )
                {
                    LambdaLogger.Log( "[RegTest] An exception was thrown while running SQL query test!" );
                    LambdaLogger.Log( Ex.ToString() );

                    Results.QueryTimeSql = 0.0d;
                    Results.State = Results.State == TestStatus.DynError ? TestStatus.BothError : TestStatus.SqlError;
                    Results.ErrorMessage += Ex + "\n";
                }

                // Delete all items in the database
                using( var Command = new MySqlCommand( "DELETE FROM TestTable", Database ) )
                {
                    try
                    {
                        Command.ExecuteNonQuery();
                    }
                    catch( Exception Ex )
                    {
                        LambdaLogger.Log( String.Format( "[RegTest] An exception was thrownw while cleaning up MySql database. {0}", Ex.Message ) );
                    }
                }
            }

            if( Results.State == TestStatus.Success )
            {
                LambdaLogger.Log( "[RegTest] All tests have completed successfully!" );
            }
            else
            {
                LambdaLogger.Log( "[RegTest] Tests complete. Error(s) occured during the test. Check output logs" );
            }

            return Results;
        }
    }
}

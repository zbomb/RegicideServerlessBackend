using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;

using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.DynamoDBv2;

using Amazon.S3.Model;
using Amazon.S3;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace S3Test
{
    public struct TestRequest
    {
        public int AccountNum { get; set; }
        public int AccountCards { get; set; }
        public int AccountDecks { get; set; }
        public int DeckCards { get; set; }
        public int AccountAchv { get; set; }
    }

    public struct TestResponse
    {
        public double InsTime { get; set; }
        public double RetTime { get; set; }
        public bool Error { get; set; }
        public string ErrMessage { get; set; }
    }

    public struct Card
    {
        public ushort Id { get; set; }
        public ushort Ct { get; set; }
    }

    public struct Deck
    {
        public string Name { get; set; }
        public uint Id { get; set; }
        public List<Card> Cards { get; set; }
    }

    [DynamoDBTable( "S3Test" )]
    public struct Account
    {
        [DynamoDBHashKey]
        public string User { get; set; }
        public uint Id { get; set; }

        public string Email { get; set; }
        public string PassHash { get; set; }
        public string DispName { get; set; }

        public string S3Link { get; set; }

        [DynamoDBIgnore]
        public List<Card> Cards { get; set; }

        [DynamoDBIgnore]
        public List<Deck> Decks { get; set; }
        
    }

    // Used to serialize for S3
    public struct AccountFile
    {
        public UInt32 Id { get; set; }
        public List<Card> Cards { get; set; }
        public List<Deck> Decks { get; set; }
    }

    public class Function
    {

        public TestResponse FunctionHandler( TestRequest Params, ILambdaContext context )
        {
            var Output = new TestResponse()
            {
                ErrMessage = String.Empty
            };

            // Build an account info
            List<Account> Accounts = new List<Account>();
            Random Rand = new Random();

            for( int i = 0; i < Params.AccountNum;  i++ )
            {
                var newAccount = new Account()
                {
                    Cards = new List<Card>(),
                    Decks = new List<Deck>()
                };

                for( int j = 0; j < Params.AccountCards; j++ )
                {
                    newAccount.Cards.Add( new Card()
                    {
                        Id = (ushort) Rand.Next( 0, UInt16.MaxValue ),
                        Ct = (ushort) Rand.Next( 0, 30 )
                    } );
                }

                for( int j = 0; j < Params.AccountDecks;  j++ )
                {
                    Deck newDeck = new Deck()
                    {
                        Name = "test deck",
                        Id = (uint) j,
                        Cards = new List<Card>()
                    };

                    for( int x = 0; x < Params.DeckCards;  x++ )
                    {
                        newDeck.Cards.Add( new Card()
                        {
                            Id = (ushort) Rand.Next( 0, UInt16.MaxValue ),
                            Ct = (ushort) Rand.Next( 0, 30 )
                        } );
                    }

                    newAccount.Decks.Add( newDeck );
                }
                newAccount.Id = (uint)i;
                newAccount.DispName = "test";
                newAccount.DispName = "dispname" + i.ToString();
                newAccount.User = "user" + i.ToString();
                newAccount.Email = "test@test.test";
                newAccount.PassHash = "asfsdfasdfasfasfdasfdsadf";
                Accounts.Add( newAccount );
            }

            using( var S3 = new AmazonS3Client() )
            {
                    // Put all accounts in S3
                    foreach( var act in Accounts )
                    {
                        // Serialize account
                        var Serializer = new Amazon.Lambda.Serialization.Json.JsonSerializer();
                        string SerAct = null;

                        AccountFile newFile = new AccountFile()
                        {
                            Id = act.Id,
                            Cards = act.Cards,
                            Decks = act.Decks
                        };

                        using( var Stream = new System.IO.MemoryStream() )
                        {
                            Serializer.Serialize( newFile, Stream );
                            SerAct = System.Text.Encoding.UTF8.GetString( Stream.ToArray() );
                        }

                        var PutReq = new PutObjectRequest()
                        {
                            BucketName = "regicidetesting",
                            Key = "user_" + act.Id,
                            ContentBody = SerAct
                        };

                        PutReq.Metadata.Add( "reg-userid", newFile.Id.ToString() );

                        var PutTask = S3.PutObjectAsync( PutReq );
                        PutTask.Wait();
                    }
            }


            return Output;
        }
    }
}

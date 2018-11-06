using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using ICSharpCode.SharpZipLib.GZip;


namespace RegicideUtils
{
    struct ContentInfo
    {
        public string Path;
        public string TargetPath;
        public long Begin;
        public long End;
    }

    public static class BlockBuilder
    {

        public static void Init()
        {
            Program.AddCommand( new Command()
            {
                Name = "build-block",
                Callback = Run
            } );
        }

        public static bool Run( string Args )
        {
            Console.WriteLine( "==================== Regicide Content Block System v0.1 ====================" );

            string LocalPath = AppDomain.CurrentDomain.BaseDirectory + "/GeneratedBlocks/";
            try
            {
                if( !Directory.Exists( LocalPath ) )
                    Directory.CreateDirectory( LocalPath );
            }
            catch( Exception Ex )
            {
                Console.WriteLine( "=> Failed to create output directory! {0}", Ex.Message );
                Console.WriteLine( "=> Utility output might not be able to write to disk" );
            }

        // Output File Name
        filename:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter output file name:" );
            Console.Write( "> " );

            string OutputFile = Console.ReadLine()?.Trim();
            if( OutputFile?.ToLower() == "q" || OutputFile?.ToLower() == "quit" )
                return false;

            if( String.IsNullOrWhiteSpace( OutputFile ) || !OutputFile.Contains( "." ) || !OutputFile.EndsWith( ".block", StringComparison.InvariantCultureIgnoreCase ) )
            {
                Console.WriteLine( "==> Please enter a valid output file name (extension must be .block)" );
                goto filename;
            }

            string FilePath = null;

            // Check if file exists
            if( File.Exists( LocalPath + "/" + OutputFile ) )
            {
                Console.WriteLine( "=> A file with this name already exists.. do you want to overwrite? (y/n)" );
            fileyn:
                Console.Write( "> " );

                string YN = Console.ReadLine()?.Trim();

                if( YN.ToLower() == "y" )
                {
                    FilePath = LocalPath + "/" + OutputFile;
                }
                else if( YN.ToLower() == "n" )
                {
                    goto filename;
                }
                else if( YN.ToLower() == "q" || YN.ToLower() == "quit" )
                {
                    return false;
                }
                else
                {
                    Console.WriteLine( "=> Unknown input.. enter 'y' to overwrite, or 'n' to enter a new filename" );
                    goto fileyn;
                }
            }
            else
            {
                FilePath = LocalPath + "/" + OutputFile;
            }

        // Identifier
        identifier:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter Identifier:" );
            Console.Write( "> " );

            string Identifier = Console.ReadLine();
            if( !String.IsNullOrEmpty( Identifier ) )
            {
                Identifier = Identifier.Trim();

                if( Identifier?.ToLower() == "q" || Identifier?.ToLower() == "quit" )
                    return false;

                if( String.IsNullOrEmpty( Identifier ) || Identifier.Length < 4 || Identifier.Length > 40
                   || !Regex.IsMatch( Identifier, "^[a-zA-Z0-9_]+$" ) )
                {
                    Console.WriteLine( "==> Invalid identifier! Must be between {0} and {1} characters and be comprised of alpha numeric characters (plus underscores)!", 4, 40 );
                    goto identifier;
                }
            }
            else
            {
                goto identifier;
            }


            List< ContentInfo > Files = new List< ContentInfo >();

            Console.WriteLine( "" );

            while( true )
            {
                Console.WriteLine( "Enter Filename (empty string to finish):" );
                Console.Write( "> " );

                string inFile = Console.ReadLine();
                if( String.IsNullOrEmpty( inFile ) )
                    break;

                // Check if the entered file exists
                if( !File.Exists( inFile ) )
                {
                    Console.WriteLine( "=> '{0}' doesnt exist!", inFile );
                    continue;
                }

                Console.WriteLine( "Enter Deploy Path: " );
                Console.Write( "> " );

                string inPath = Console.ReadLine()?.Trim()?.ToLower();
                if( inPath == "q" || inPath == "quit" )
                    return true;

                if( String.IsNullOrEmpty( inPath ) )
                {
                    Console.WriteLine( "Canceling this file!" );
                    continue;
                }

                ContentInfo newInfo = new ContentInfo();
                newInfo.Path = inFile;
                newInfo.TargetPath = inPath;

                Files.Add( newInfo );
                Console.WriteLine( "=> '{0}' added to block!", newInfo.Path );
                Console.WriteLine( "" );
            }

            if( Files.Count == 0 )
            {
                Console.WriteLine( "=> Error! No Files Entered!" );
                return true;
            }

            Console.WriteLine( "=> Starting..." );

            long FinalSize = 0;
            foreach( var Info in Files )
            {
                FileInfo info = new FileInfo( Info.Path );
                if( info == null || !info.Exists )
                {
                    Console.WriteLine( "=> File '{0}' no longer exists! Operation failed!", Info.Path );
                    return true;

                }

                if( info.Length == 0 )
                {
                    Console.WriteLine( "=> File: '{0}' is empty! Skipping..." );
                    continue;
                }

                FinalSize += info.Length;
            }

            byte[] FinalBlob = new byte[ FinalSize ];
            long CurrentOffset = 0;
            List<ContentInfo> Output = new List<ContentInfo>();

            for( int i = 0; i < Files.Count; i++ )
            {
                var Data = File.ReadAllBytes( Files[ i ].Path );
                if( Data == null || Data.Length == 0 )
                {
                    Console.WriteLine( "=> File '{0}' couldnt be read! Operation failed!", Files[ i ].Path );
                    return true;
                }

                ContentInfo newInfo = new ContentInfo()
                {
                    TargetPath = Files[ i ].TargetPath,
                    Begin = CurrentOffset,
                    End = CurrentOffset + Data.Length
                };

                Console.WriteLine( "=> Packed file '{0}' ({1} bytes)", Files[ i ].Path, Data.Length );
                Console.WriteLine( "" );

                Output.Add( newInfo );
                Array.ConstrainedCopy( Data, 0, FinalBlob, (int)newInfo.Begin, Data.Length );

                // Advance Offset
                CurrentOffset += Data.Length;
            }

            // Create Json header
            StringBuilder Builder = new StringBuilder();
            StringWriter Writer = new StringWriter( Builder );

            using( JsonWriter jWrite = new JsonTextWriter( Writer ) )
            {
                jWrite.Formatting = Formatting.Indented;

                jWrite.WriteStartObject();
                jWrite.WritePropertyName( "Id" );
                jWrite.WriteValue( Identifier );

                jWrite.WritePropertyName( "Files" );
                jWrite.WriteStartArray();

                // Loop through files and write entry for each
                foreach( var F in Output )
                {
                    jWrite.WriteStartObject();
                    jWrite.WritePropertyName( "File" );
                    jWrite.WriteValue( F.TargetPath );
                    jWrite.WritePropertyName( "Begin" );
                    jWrite.WriteValue( F.Begin );
                    jWrite.WritePropertyName( "End" );
                    jWrite.WriteValue( F.End );
                    jWrite.WriteEndObject();
                }

                jWrite.WriteEndArray();
            }

            string Header = Builder.ToString();

            // Now, we need to build the final file
            byte[] HeaderData = Encoding.UTF8.GetBytes( Header );
            int HeaderLength = HeaderData.Length;

            byte[] FinalFile = new byte[ FinalBlob.Length + HeaderLength + 4 ];
            byte[] HeaderLenData = BitConverter.GetBytes( HeaderLength );

            if( !BitConverter.IsLittleEndian )
                Array.Reverse( HeaderLenData );

            Array.ConstrainedCopy( HeaderLenData, 0, FinalFile, 0, 4 );
            Array.ConstrainedCopy( HeaderData, 0, FinalFile, 4, HeaderLength );
            Array.ConstrainedCopy( FinalBlob, 0, FinalFile, HeaderLength + 4, FinalBlob.Length );

            Console.WriteLine( "=> Block built.." );

            // Compute hash of the block
            string Hash;
            using( var SHA = SHA256.Create() )
            {
                Hash = Convert.ToBase64String( SHA.ComputeHash( FinalFile ) );
            }

            Console.WriteLine( "=> Block hashed.." );

            // Compress the block
            byte[] CompressedFile = null;
            using( var inStream = new MemoryStream( FinalFile ) )
            {
                using( var outStream = new MemoryStream() )
                {
                    GZip.Compress( inStream, outStream, true );
                    CompressedFile = outStream.ToArray();
                }
            }

            Console.WriteLine( "=> Block compressed ({0} bytes -> {1} bytes)", FinalFile.Length, CompressedFile.Length );

            // Finally, write to file
            File.WriteAllBytes( LocalPath + OutputFile, CompressedFile );
            Console.WriteLine( "=> New block written! {0} ({1} bytes)", LocalPath + OutputFile, CompressedFile.Length );

            // Create json data that needs to be added to the mainfest
            StringBuilder sb = new StringBuilder();
            StringWriter wr = new StringWriter( sb );
            using( JsonWriter jWrite = new JsonTextWriter( wr ) )
            {
                jWrite.Formatting = Formatting.Indented;

                jWrite.WriteComment( "Manually generated block. Add to the 'Entries' list in the manifest." );
                jWrite.WriteRaw( "\n" );
                jWrite.WriteStartObject();
                jWrite.WritePropertyName( "URL" );
                jWrite.WriteValue( "<URL Path>" );
                jWrite.WritePropertyName( "Id" );
                jWrite.WriteValue( Identifier );
                jWrite.WritePropertyName( "Hash" );
                jWrite.WriteValue( Hash );
                jWrite.WritePropertyName( "Size" );
                jWrite.WriteValue( CompressedFile.Length );
                jWrite.WriteEndObject();
            }

            string ManifestEntry = sb.ToString();

            string noExt = OutputFile.Remove( OutputFile.LastIndexOf( '.' ) );
            File.WriteAllText( LocalPath + noExt + "_manifest.txt", ManifestEntry );
            Console.WriteLine( "=> Wrote manifest entry! {0}", LocalPath + noExt + "_manifest.txt" );
            Console.WriteLine( "=> Complete!" );
            Console.WriteLine( "" );
            return true;

        }
    }
}

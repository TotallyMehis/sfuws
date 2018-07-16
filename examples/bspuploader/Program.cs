using System;
using System.Net;
using System.IO;

using ICSharpCode.SharpZipLib.BZip2;

using SFUWS.InputReader;
using SFUWS.WebServer;
using SFUWS.HTMLOutput;


namespace ConsoleApplication1
{
    // Lets users upload .bsp and .bsp.bz2 files.
    // The application then compresses/decompresses the files and places them in selected directories.
    class Program
    {
        private static long MaxContentLength = 100000000; // In bytes (100MB by default)
        private static string[] URIPrefixes = { "http://localhost:8080/mapupload/" };
        private static int MaxInputsToRead = 2; // How many inputs we'll allow. Please note that password is also one.
        private static string Bz2OutputDir = "";
        private static string BspOutputDir = "";
        private static string Password = "";

        private static void ParseArgs( string[] args )
        {
            int len = args.Length;
            for ( int i = 0; i < len; i++ )
            {
                if ( args[i][0] != '-' )
                    continue;


                int j = i + 1;
                bool bHasValue = j < len;

                string arg = args[i].Substring( 1 );
                if ( arg == "maxcontentlength" && bHasValue )
                {
                    MaxContentLength = long.Parse( args[j].Trim() );
                }
                else if ( arg == "maxinputs" && bHasValue )
                {
                    MaxInputsToRead = int.Parse( args[j].Trim() );
                }
                else if ( arg == "bz2dir" && bHasValue )
                {
                    Bz2OutputDir = args[j].Trim();
                }
                else if ( arg == "bspdir" && bHasValue )
                {
                    BspOutputDir = args[j].Trim();
                }
                else if ( arg == "uris" && bHasValue )
                {
                    URIPrefixes = args[j].Trim().Split( ',', ';' );
                }
                else if ( arg == "password" && bHasValue )
                {
                    Password = args[j].Trim();
                }

                if ( bHasValue )
                    ++i;
            }
        }

        static void Main( string[] args )
        {
            ParseArgs( args );


            WebServer srv = new WebServer( URIPrefixes, new MapOutput() );
            srv.Run();

            Console.WriteLine( "Running server..." );

            Console.ReadKey();

            Console.WriteLine( "Ending..." );
        }

        class MapOutput : HTMLOutput
        {
            // For making sure the password is correct and only bsp/bz2 files are uploaded.
            class MapValidator : FormInputValidator
            {
                public override bool IsValidInput( FormInput input, int index, bool isStillReading )
                {
                    // Check password which should always be the first input!!
                    if ( index != 0 )
                        return true;


                    // We'll return when we have the full value.
                    if ( isStillReading && input.Value.Length < Password.Length )
                        return true;


                    return Password == "" || input.Value == Password;
                }

                // TODO: Check magic number.
                // Very lazy check for now, only see if extension is valid.
                public override bool IsValidFileName( string fileName )
                {
                    string check = ".bsp.bz2";
                    string check2 = ".bsp";
                
                    int i = fileName.IndexOf( check );
                    int i2 = fileName.IndexOf( check2 );
                    int j = (fileName.Length - check.Length);
                    int j2 = (fileName.Length - check2.Length);

                    return  (i != -1 && i == j)
                        ||  (i2 != -1 && i2 == j2);
                }
            }

            public override string OnRequest( HttpListenerRequest request )
            {
                //Console.WriteLine( "Content type: {0}, Encoding: {1}, Content len: {2}",
                //    request.ContentType,
                //    request.ContentEncoding,
                //    request.ContentLength64 );

                if ( request.ContentLength64 > 0 && request.ContentLength64 <= MaxContentLength )
                {
                    return HandleContentRequest( request );
                }


                return FileOutput( "index.html" );
            }

            private string HandleContentRequest( HttpListenerRequest request )
            {
                // Read the sent data and save them to temporary files.
                using ( InputReader read = new InputReader(
                        request.ContentType,
                        MaxInputsToRead,
                        MaxContentLength,
                        new MapValidator() ) )
                {
                    string errorStr = "";
                    try
                    {
                        read.ReadStream( request.InputStream );
                    }
                    catch ( ValidatorException )
                    {
                        errorStr = "Failed! Make sure you typed password right and uploaded .bsp/.bz2 files only.";
                    }
                    catch ( Exception e )
                    {
                        Console.WriteLine( "Error reading input stream: " + e.Message );

                        errorStr = "Something went wrong.";
                    }

                    request.InputStream.Close();


                    // Save the map files to target dirs.
                    return ( errorStr == "" ) ? SaveFiles( read ) : errorStr;
                }
            }
        } // MapOutput

        private static string SaveFiles( InputReader parser )
        {
            // No inputs were parsed.
            if ( !parser.HasInputs )
            {
                return "No files uploaded! :(";
            }

            // First input should always be the password.
            if ( parser.Inputs[0].Name != "password" )
                return "No password.";


            bool bState = false;
            foreach ( var inp in parser.Inputs )
            {
                if ( !inp.IsFile() )
                    continue;


                var file = inp as FormInputFile;


                // Doesn't strip all extensions. Will leave .bsp if .bsp.bz2
                string ext = Path.GetExtension( file.FileName );
                if ( ext == ".bz2" )
                {
                    bState = SaveFromCompressedFile( file );
                }
                else if ( ext == ".bsp" )
                {
                    bState = SaveFromUncompressedFile( file );
                }

                if ( !bState )
                    break;
            }

            return bState ? "Success!" : "Failed to save files :(";
        }

        // Ugly, but gets the job done.
        private static bool SaveFromCompressedFile( FormInputFile file )
        {
            // Copies temporary file to target dir.
            // Uncompresses the .bsp.bz2 to target dir.
            string name = Path.GetFileNameWithoutExtension( file.FileName );
            string ext = Path.GetExtension( file.FileName );
            try
            {
                string bspOutput = Path.Combine( BspOutputDir, name );

                using ( var input = File.OpenRead( file.TempFilePath ) )
                using ( var output = File.Open( bspOutput, FileMode.CreateNew ) )
                {
                    BZip2.Decompress( input, output, false );
                }

                File.Copy( file.TempFilePath, Path.Combine( Bz2OutputDir, file.FileName ) );

                Console.WriteLine( "Saved " + name );
            }
            catch
            {
                Console.WriteLine( "Failed to save from compressed file!" );
                return false;
            }

            return true;
        }

        private static bool SaveFromUncompressedFile( FormInputFile file )
        {
            // Copies temporary file to target dir.
            // Compresses .bsp to target dir.
            string name = Path.GetFileNameWithoutExtension( file.FileName );
            string ext = Path.GetExtension( file.FileName );
            try
            {
                string bz2Output = Path.Combine( Bz2OutputDir, name + ".bsp.bz2" );

                using ( var input = File.OpenRead( file.TempFilePath ) )
                using ( var output = File.Open( bz2Output, FileMode.CreateNew ) )
                {
                    BZip2.Compress( input, output, false, 5 );
                }

                File.Copy( file.TempFilePath, Path.Combine( BspOutputDir, name + ".bsp" ) );

                Console.WriteLine( "Saved " + name );
            }
            catch
            {
                Console.WriteLine( "Failed to save from uncompressed file!" );
                return false;
            }

            return true;
        }
    }
}

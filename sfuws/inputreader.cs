using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Net;

//
// For reading inputs from the POST request.
//
namespace SFUWS.InputReader
{
    public class ValidatorException : Exception
    {
        public ValidatorException( string msg ) : base( msg ) { }
    }

    public class FormInputFile : FormInput
    {
        public readonly string FileName;
        public readonly string TempFilePath;

        public FormInputFile( string name, string fileName, string tempFilePath ) : base( name, "" )
        {
            FileName = fileName;
            TempFilePath = tempFilePath;
        }

        public override void Close()
        {
            try
            {
                File.Delete( TempFilePath );
            }
            catch
            {
                Console.WriteLine( "Failed to remove temp file!" );
            }
        }

        public override bool IsFile() { return true; }
    }

    public class FormInput
    {
        private bool _locked = false;
        private string _value;

        public readonly string Name;
        public string Value
        {
            get { return _value; }
            set
            {
                if ( !IsLocked )
                    _value = value;
            }
        }
        public bool IsLocked
        {
            get { return _locked; }
            set
            {
                if ( !_locked )
                    return;

                _locked = value;
            }
        }

        public FormInput( string name, string value = "" )
        {
            Name = name;
            Value = value;
        }

        public virtual void Close()
        {

        }

        public virtual bool IsFile() { return false; }
    }

    public class FormInputValidator
    {
        public virtual bool IsValidInput( FormInput input, int index, bool isStillReading )
        {
            return true;
        }

        public virtual bool HasEnoughBytes( long numBytes )
        {
            return true;
        }

        public virtual bool IsValidFileName( string fileName )
        {
            return true;
        }

        public virtual bool IsValidFile( FileStream strm, string fileName )
        {
            return true;
        }
    }

    // Helper class
    class HttpStreamReader
    {
        private readonly Stream _strm;

        public bool IsEnd { get; private set; } = false;
        public bool IsErrored { get; private set; } = false;

        public HttpStreamReader( Stream strm )
        {
            _strm = strm;
        }

        public int Read( byte[] array, int offset, int count )
        {
            if ( IsEnd )
                return 0;


            int read = 0;

            try
            {
                read = _strm.Read( array, offset, count );
            }
            catch
            {
                IsErrored = true;
                IsEnd = true;

                Console.WriteLine( "Failed to read from request stream!" );
            }

            if ( read != count )
                IsEnd = true;

            return read;
        }
    }

    // Helper class
    class TemporaryStream
    {
        public FileStream _strm { get; private set; } = null;
        public long NumBytes { get; private set; } = 0;
        public string TempFile { get; private set; } = "";
        public bool DeleteFileOnDispose { get; set; } = false;

        public TemporaryStream()
        {
        }

        public void OpenFile()
        {
            // We haven't written here yet, just keep it open.
            if ( _strm != null && NumBytes == 0 )
            {
                return;
            }


            if ( _strm != null )
            {
                _strm.Close();
            }

            TempFile = Path.GetTempFileName();
            _strm = File.Open( TempFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
            NumBytes = 0;

            DeleteFileOnDispose = true;
        }

        public void Write( byte[] array, int startindex, int count )
        {
            _strm.Write( array, startindex, count );
            NumBytes += count - startindex;
        }

        public void Close()
        {
            _strm.Close();


            if ( DeleteFileOnDispose )
            {
                try
                {
                    File.Delete( TempFile );
                }
                catch { }
            }
        }
    }

    public class InputReader : IDisposable
    {
        public bool IsDone { get; private set; } = false;

        public readonly string Boundary;

        public readonly int MaxInputsToRead;
        public readonly long MaxContentLength; // In bytes

        private readonly FormInputValidator CheckFunc;

        private readonly string LineBreak = "\r\n";

        public List<FormInput> Inputs { get; private set; } = new List<FormInput>();
        public bool HasInputs { get { return Inputs.Count > 0; } }


        public InputReader( string contentType, Stream strm )
        {
            Boundary = ParseBoundary( contentType );
            MaxInputsToRead = 1;
            MaxContentLength = 70000000;
            CheckFunc = new FormInputValidator();
        }

        
        public InputReader( string contentType, int maxInputsToRead, long maxBytesToRead, FormInputValidator func )
        {
            // Content type will include the boundary which we need for parsing the post request.
            Boundary = ParseBoundary( contentType );
            MaxInputsToRead = maxInputsToRead;
            MaxContentLength = maxBytesToRead;
            CheckFunc = func;
        }

        public void Dispose()
        {
            foreach ( var file in Inputs )
            {
                file.Close();
            }
        }

        public void ReadStream( Stream requestStream )
        {
            if ( IsDone )
                return;


            IsDone = true;

            if ( Boundary.Length < 1 )
                throw new Exception( "Invalid boundary!" );

            // This is the sequence of characters we'll be looking out for.
            string endBoundary = Boundary + "--";

            // This is the break between the header and the payload itself. Should be two line breaks CRLFCRLF
            string headerEnd = LineBreak + LineBreak;

            // We'll be writing the output in a temporary file.
            // We can't simply read the entire request, what if the request overflows our max string length...
            var tempStrm = new TemporaryStream();

            const int bufferSize = 1024;
            const int halfBufferSize = bufferSize / 2;


            var strm = new HttpStreamReader( requestStream );


            long bytesRead = 0;


            bool bFindStart = true;

            byte[] buffer = new byte[bufferSize];
            FormInput curInput = null;
            
            int iBufferOffset = Boundary.Length + LineBreak.Length;

            do
            {
                int len = strm.Read( buffer, 0, halfBufferSize );

                //Console.Write( Encoding.ASCII.GetString( buffer, 0, len ) );

                
                // Keep parsing this buffer until we need to read more.
                while ( true )
                {
                    // Constantly be on the look out for the ending boundary.
                    int iEndBoundary = -1;
                    int iBoundary = IndexOf( buffer, iBufferOffset, len, Boundary );
                    if ( iBoundary != -1 )
                    {
                        iEndBoundary = IndexOfFromEnd( buffer, iBoundary, LineBreak );
                    }
                    else if ( !strm.IsEnd )
                    {
                        // Check if our boundary has been cut off.
                        int cut = IsCutOff( buffer, len, Boundary );
                        if ( cut != -1 )
                        {
                            // We may be cut off, read a bit more.
                            int matched = len - cut;
                            int readSize = endBoundary.Length - matched;


                            int read = strm.Read( buffer, len, readSize );

                            iBoundary = IndexOf( buffer, len-matched, len+read, Boundary );
                            if ( iBoundary != -1 )
                            {
                                iEndBoundary = IndexOfFromEnd( buffer, iBoundary, LineBreak );
                            }

                            len += read;
                        }
                    }


                    if ( bFindStart )
                    {
                        // We need to be able to see the entire header before parsing it.
                        int payloadStart = IndexOf( buffer, iBufferOffset, len, headerEnd );

                        int lenToParse = payloadStart - iBufferOffset;

                        // No header end found, read a bit more to see if we can spot it.
                        if ( payloadStart == -1 )
                        {
                            const int readSize = 256;
                            int read = strm.Read( buffer, len, readSize );

                            len += read;

                            payloadStart = IndexOf( buffer, iBufferOffset, len, headerEnd );

                            lenToParse = payloadStart - iBufferOffset;
                        }


                        if ( payloadStart == -1 )
                        {
                            throw new Exception( "Couldn't find payload start!" );
                        }

                        if ( lenToParse <= 0 )
                        {
                            throw new Exception( "No header to parse!" );
                        }


                        payloadStart += headerEnd.Length;


                        string parse = Encoding.ASCII.GetString( buffer, iBufferOffset, lenToParse );


                        string name = ParseName( parse );
                        if ( name == null )
                        {
                            throw new Exception( "Request header had no input name!" );
                        }


                        string fileName = ParseFilename( parse );

                        if ( fileName == "" )
                        {
                            throw new Exception( "Not a valid filename!" );
                        }


                        if ( fileName == null )
                        {
                            curInput = new FormInput( name );
                        }
                        else
                        {
                            if ( !CheckFunc.IsValidFileName( fileName ) )
                                throw new Exception( "Invalid file name!" );


                            tempStrm.OpenFile();
                            curInput = new FormInputFile( name, fileName, tempStrm.TempFile );
                        }

                        


                        int readContentSize = len - payloadStart;
                        if ( iEndBoundary != -1 )
                            readContentSize = iEndBoundary - payloadStart;


                        if ( readContentSize > 0 )
                        {
                            if ( curInput.IsFile() )
                                tempStrm.Write( buffer, payloadStart, readContentSize );
                            else
                                curInput.Value += Encoding.ASCII.GetString( buffer, payloadStart, readContentSize );
                        }


                        bFindStart = false;
                    }
                    // We're still in the middle of reading the content, keep going.
                    else
                    {
                        int readContentSize = ( iEndBoundary != -1 ) ? iEndBoundary : len;


                        if ( readContentSize > 0 )
                        {
                            if ( curInput.IsFile() )
                                tempStrm.Write( buffer, 0, readContentSize );
                            else
                                curInput.Value += Encoding.ASCII.GetString( buffer, 0, readContentSize );
                        }
                        
                    }
                    

                    if ( !CheckFunc.IsValidInput( curInput, Inputs.Count, iEndBoundary == -1 ) )
                    {
                        throw new ValidatorException( "Input did not pass validator function!" );
                    }

                    if ( curInput.IsFile() )
                    {
                        var f = curInput as FormInputFile;
                        if (CheckFunc.HasEnoughBytes( tempStrm.NumBytes )
                        &&  !CheckFunc.IsValidFile( tempStrm._strm, f.FileName ) )
                        {
                            throw new ValidatorException( "File did not pass validator function!" );
                        }
                    }


                    // Make sure we're at the end to keep writing.
                    tempStrm._strm?.Seek( 0, SeekOrigin.End );
                

                    // We've encountered the end of an input or the stream.
                    if ( iEndBoundary != -1 || strm.IsEnd )
                    {
                        tempStrm.DeleteFileOnDispose = false;

                        // Lock and save
                        curInput.IsLocked = true;
                        Inputs.Add( curInput );
                        curInput = null;


                        bFindStart = true;

                        
                        if ( Inputs.Count >= MaxInputsToRead )
                            break;
                    }

                    if ( strm.IsEnd )
                        break;

                    if ( iBoundary != -1 )
                    {
                        iBufferOffset = iBoundary + Boundary.Length + LineBreak.Length;
                    }
                    else
                    {
                        iBufferOffset = 0;
                        break;
                    }
                }
                
                
                bytesRead += len;


                if ( Inputs.Count >= MaxInputsToRead )
                    break;
            }
            while ( !strm.IsEnd && bytesRead < MaxContentLength );

            tempStrm.Close();
        }
        
        private string ParseBoundary( string contentType )
        {
            string find = "boundary=";
            int i = contentType.IndexOf( find );
            if ( i != -1 )
            {
                i += find.Length;
            }


            if ( i == -1 || i >= contentType.Length )
                return "";

            return contentType.Substring( i ).Trim();
        }

        // Returns null if no name found in string.
        private string ParseName( string buffer )
        {
            string findname = "name=";

            int namePos = buffer.IndexOf( findname );
            if ( namePos == -1 )
                return null;

            namePos += findname.Length;


            char[] ends = { ';', ' ', '\r', '\n' };
            int nameEndPos = buffer.IndexOfAny( ends, namePos );
            if ( nameEndPos == -1 )
                nameEndPos = buffer.Length; // Just assume the rest of the buffer.


            char[] trims = { '\"', '\'' };

            string ret = null;

            try
            {
                ret = buffer.Substring( namePos, nameEndPos - namePos ).Trim( trims );
            }
            catch { }
            

            return ret;
        }

        // Returns null if not a file.
        // Returns empty string if errored.
        private string ParseFilename( string buffer )
        {
            string findfilename = "filename=";

            int fileNamePos = buffer.IndexOf( findfilename );
            if ( fileNamePos == -1 )
                return null;

            fileNamePos += findfilename.Length;


            char[] ends = { ';', ' ', '\r', '\n' };
            int fileNameEndPos = buffer.IndexOfAny( ends, fileNamePos );
            if ( fileNameEndPos == -1 )
                return "";


            char[] trims = { '\"', '\'' };

            string ret = "";

            try
            {
                ret = Path.GetFileName( buffer.Substring( fileNamePos, fileNameEndPos - fileNamePos ).Trim( trims ) );
            }
            catch { }
            

            return ret;
        }

        private static void CopyEndToStart( byte[] array, int count )
        {
            for ( int i = 0; i < count; i++ )
            {
                array[i] = array[count-(count-i)];
            }
        }

        private static int IndexOf( byte[] buffer, int startindex, int len, string find )
        {
            int findLength = find.Length;
            for ( int i = startindex; i < len - findLength; i++ )
            {
                bool match = true;
                for ( int j = 0; j < findLength && match; j++ )
                {
                    match = buffer[i + j] == find[j];
                }

                if ( match )
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfFromEnd( byte[] buffer, int len, string find )
        {
            int findLength = find.Length;
            for ( int i = len - findLength; i >= 0; i-- )
            {
                bool match = true;
                for ( int j = 0; j < findLength && match; j++ )
                {
                    match = buffer[i + j] == find[j];
                }

                if ( match )
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IsCutOff( byte[] buffer, int len, string find )
        {
            int findLength = find.Length;
            for ( int i = len - findLength; i < len; i++ )
            {
                bool match = true;
                for ( int j = 0; j < findLength && match; j++ )
                {
                    int k = i + j;
                    if ( k >= len )
                       break;

                    match = buffer[k] == find[j];
                }

                if ( match )
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

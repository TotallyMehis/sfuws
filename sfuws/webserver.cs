using System;
using System.Net;
using System.Threading;
using System.Text;

using SFUWS.HTMLOutput;

namespace SFUWS.WebServer
{
    /*
        The MIT License (MIT)

        Copyright (c) 2013 David's Blog (www.codehosting.net) 

        Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
        associated documentation files (the "Software"), to deal in the Software without restriction, 
        including without limitation the rights to use, copy, modify, merge, publish, distribute, 
        sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is 
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all copies or 
        substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, 
        INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
        PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE 
        FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR 
        OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
        DEALINGS IN THE SOFTWARE.
    */
    public class WebServer
    {
        private readonly HttpListener _lstnr = new HttpListener();
        private readonly HTMLOutput.HTMLOutput _response;

        public WebServer( string[] prefixes, HTMLOutput.HTMLOutput output )
        {
            if ( !HttpListener.IsSupported )
            {
                throw new NotSupportedException( "HttpListener not supported by this system." );
            }

            if ( prefixes.Length <= 0 )
            {
                throw new Exception( "No prefixes" );
            }

            
            foreach ( var prefix in prefixes )
            {
                Console.WriteLine( "Listening at " + prefix );
                _lstnr.Prefixes.Add( prefix );
            }
                

            _response = output;

            _lstnr.Start();
        }

        public void Run()
        {
            ThreadPool.QueueUserWorkItem( (o) =>
            {
                try
                {
                    while ( _lstnr.IsListening )
                    {
                        ThreadPool.QueueUserWorkItem( (c) =>
                        {
                            var ctx = c as HttpListenerContext;

                            try
                            {
                                string str = _response.OnRequest( ctx.Request );
                                byte[] buf = Encoding.UTF8.GetBytes( str );

                                ctx.Response.ContentLength64 = buf.Length;

                                ctx.Response.OutputStream.Write( buf, 0, buf.Length );
                            }
                            catch { }
                            finally
                            {
                                ctx.Response.OutputStream.Close();
                            }
                        }, _lstnr.GetContext() );
                    }
                }
                catch { }
            } );
        }

        public void Stop()
        {
            _lstnr.Stop();
            _lstnr.Close();
        }
    }
}

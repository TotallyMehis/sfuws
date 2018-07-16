using System;
using System.IO;
using System.Net;

namespace SFUWS.HTMLOutput
{
    // Useful helper class to make file output easier.
    public abstract class HTMLOutput
    {
        protected string LastFileName = "";
        protected DateTime LastHTMLFileChange = DateTime.MinValue;
        protected string StringOutput = "";

        public virtual string OnRequest( HttpListenerRequest request )
        {
            return "";
        }

        public virtual string FileOutput( string fileName )
        {
            DateTime newtime;

            try
            {
                newtime = File.GetLastWriteTime( fileName );
            }
            catch
            {
                newtime = DateTime.MinValue;
            }


            if ( newtime != LastHTMLFileChange )
            {
                try
                {
                    StringOutput = File.ReadAllText( fileName );
                }
                catch { }

                LastHTMLFileChange = newtime;
                LastFileName = fileName;
            }

            return StringOutput;
        }
    }
}

# Simple File Upload Web Server (SFUWS)
.NET library build with VS 2017 that allows easy POST-request parsing.

Don't bother using this. It's just a C# exercise.


## Test: bspuploader
Lets users upload .bsp and .bsp.bz2 files to the host. The application then compresses/decompresses the files and places them in selected directories.


### Building bspuploader example
1. Build sfuws/sfuws project
2. Put thirdparty [SharpZipLib dll](https://icsharpcode.github.io/SharpZipLib/) in examples/bspuploader/thirdparty
3. Build bspuploader project
4. Configure and run bin/run.bat

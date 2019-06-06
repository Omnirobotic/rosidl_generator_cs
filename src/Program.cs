using System;
using System.CodeDom;
using System.IO;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace rosidl_generator_cs
{
	class MainClass
	{
		public static void PrintHelp ()
		{
			Console.WriteLine ("ROS2CSMessageGenerator version: " + typeof(MainClass).Assembly.GetName ().Version);
			Console.WriteLine ("This tool generates a C# assembly from ROS2 message definitions");
			Console.WriteLine ("Usage: ");
			Console.WriteLine ("  Parse message file and generate cs code:");
			Console.WriteLine ("     mono ROS2CSMessageGenerator.exe -m <path to message file> <package name> <output path>");
			Console.WriteLine ("  Compile generated cs files to assembly:");
			Console.WriteLine ("     mono ROS2CSMessageGenerator.exe -c <directory with cs files> <path to resulting assembly>");
			

		}
		//TODO increase perfomance by parsing all messages in a package at once
		public static void Main (string[] args)
		{
		    //TestMethod();

			bool IsService = false;
			//Check the amount of arguments
			if (args.Length < 1) {
				PrintHelp ();
				return;
			}
			if (args [0] == "-m") {
				//-m means we want te generate messages
				if (args.Length < 4) {
					PrintHelp ();
					return;
				}
				string messageFile = args [1];
				string packageName = args [2];
				string outputPath = args [3];
				//Check if the output paths exists
				if (!Directory.Exists (outputPath))
					Directory.CreateDirectory (outputPath);

				Console.ForegroundColor = ConsoleColor.Blue;
				Console.WriteLine ("Parsing message file: " + messageFile);
				Console.ResetColor ();
				//Determine if we are processing a service
				if (messageFile.Contains ("Request") || messageFile.Contains ("Response")) {
					IsService = true;
				}
				//We dont need to process to srv file. Just the two messages resulting by the srv file
				if (Path.GetExtension (messageFile) == ".srv")
					return;

				//Get the message name
				string name = Path.GetFileName (messageFile);
				//Remove the extenstion
				name = name.Replace (Path.GetExtension (messageFile), "");
				//Generate a message description
				MessageDescription description = new MessageDescription (messageFile, packageName);
				//Parse the message
				MessageParser parser = new MessageParser (description);
				try {
					parser.Parse ();
				} catch (Exception ex) {
					//Parsing went wrong
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine ("Exception parsing: " + messageFile);
					Console.ResetColor ();
					Console.WriteLine (ex.ToString ());
					Environment.Exit (1);
				}
				//Generate code from the description
				IMessageCodeGenerator codeGenerator = new CodeDomMessageGenerator ();
				codeGenerator.GenerateCode (description);

				//Write the generated code to a text file
				if (!IsService)
					System.IO.File.WriteAllText (Path.Combine (outputPath, description.Name + "_msg.cs"), codeGenerator.GetGeneratedCode ());
				else
					System.IO.File.WriteAllText (Path.Combine (outputPath, description.Name + "_srv.cs"), codeGenerator.GetGeneratedCode ());
			} 
			else if (args [0] == "-c") {
				//-c means we want to compile a message package
				if (args.Length < 3) {
					PrintHelp ();
					return;
				}
            
				//The directory the .cs files lay in
				string classDir = args [1];
				//This converts the / to \ on windows and leaves them / on linux
				classDir = Path.GetFullPath (classDir);
				Console.WriteLine ("Input directory: " + classDir);
               
				//The path we want to place the resulting assembly in
				string assemblyPath = args [2];
				assemblyPath = Path.GetFullPath (assemblyPath);
				Console.WriteLine ("Assembly Path: " + assemblyPath);
              	
				//The passed directory did not exists
				if (!Directory.Exists (classDir)) {
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine ("Directory does not exist: " + classDir);
					Console.ResetColor ();
					return;
				}
				//Get all C# files in the directory
				List<string> cs_files = new List<string> ();
				foreach (var item in Directory.GetFiles(classDir)) {
					FileInfo info = new FileInfo (item);
					if (info.Extension == ".cs") {
						cs_files.Add (item);
					}
				}
				//Compile them
				CompileToAssembly (assemblyPath, cs_files);
			} 
			else {
				PrintHelp ();
			}
			Console.WriteLine ("");

		}

		public static void CompileToAssembly (string AssemblyPath, List<string> files)
		{

			CompilerParameters cp = new CompilerParameters ();

			//We need to allow unsafe code
			cp.CompilerOptions += " /unsafe ";

			string AssemblyName = Path.GetFileNameWithoutExtension (AssemblyPath);

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Compiling: " + AssemblyName);
            Console.ResetColor();

			CSharpCodeProvider provider = new CSharpCodeProvider ();

			//Retrieve search paths from the AMENT_PREFIX_PATH
			string rclcsPath = Environment.GetEnvironmentVariable ("AMENT_PREFIX_PATH");
			Console.WriteLine ("Ament Prefix Path: " + rclcsPath);

			string[] pathElements;
			//On linux paths variables are seperated by : on windows by ;
			if (Environment.OSVersion.Platform == PlatformID.Unix) {
				pathElements = rclcsPath.Split (new char[] { ':' });
			} else {
				pathElements = rclcsPath.Split (new char[] { ';' });
			}

			//We need to reference the System.dll
			cp.ReferencedAssemblies.Add ("System.dll");

			//For all elements in the AMENT_PREFIX_PATH
			foreach (var pathElement in pathElements) {


                Action<string> searchPath = (string path) =>
                {
                    // OMNI_UV
                    if (Directory.Exists(path))
                    {
                        // OMNI_UV
                        foreach (var item in Directory.GetFiles(path))
                        {
                            //A dll could be an assembly
                            if (Path.GetExtension(item) == ".dll")
                            {
                                try
                                {
                                    //Try loading the assembly -> if it works it is an assembly if not it's just normal dll
                                    System.Reflection.AssemblyName testAssembly =
                                        System.Reflection.AssemblyName.GetAssemblyName(item);
                                    Console.WriteLine(testAssembly.FullName);
                                    if (testAssembly.Name != AssemblyName)
                                        cp.ReferencedAssemblies.Add(item);
                                }
                                catch (Exception ex)
                                {
                                    //Ignore warning...
                                    ex.ToString();
                                }
                            }
                        }
                    // OMNI_UV
                    }
                    // OMNI_UV
                };

				//Look into the lib folder
				string ros2libPath = Path.Combine(pathElement, "lib");

				Console.WriteLine("ros2 libs path: " + ros2libPath);
                //Check the files
                searchPath(ros2libPath); //Search lib folder
                if(Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    //on windows search bin folder
                    ros2libPath = Path.Combine(pathElement, "bin");
                    searchPath(ros2libPath);
                }


			}
			//messages allways should result in a library
			cp.GenerateExecutable = false;
			cp.OutputAssembly = AssemblyPath;
			cp.GenerateInMemory = false;

			try {
				//Compile it
				CompilerResults results = Compile (cp, files, provider);

				if (results.Errors.Count > 0) {

					// Display compilation errors.
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine ("Errors/Warnings building: " + AssemblyPath);
					Console.ResetColor ();

					Console.WriteLine ("AMENT_PREFIX_PATH was: " + rclcsPath);
					foreach (CompilerError ce in results.Errors) {
						if (ce.IsWarning) {
							Console.ForegroundColor = ConsoleColor.Blue;
							Console.WriteLine (ce.FileName + " " + ce.ErrorNumber);
							Console.ResetColor ();
							Console.WriteLine ("  {0}", ce.ToString ());
							Console.WriteLine ();
						} else {

							Console.ForegroundColor = ConsoleColor.DarkRed;
							Console.WriteLine (ce.FileName + " " + ce.ErrorNumber);
							Console.ResetColor ();
							Console.WriteLine ("  {0}", ce.ToString ());
							Console.WriteLine ();
						}

					}
				} else {
					Console.WriteLine (results.PathToAssembly + " build successfull");

				}
			} catch (Exception ex) {
				Console.WriteLine (ex.ToString ());
			}

		}

		public static CompilerResults Compile (CompilerParameters cp, List<String> files, CSharpCodeProvider provider)
		{
			return  provider.CompileAssemblyFromFile (cp, files.ToArray ());
		}/*

	    public struct rosidl_generator_c__primitive_array_float64
        {
            IntPtr test3;

            public void SetTest3(int @int)
            {
                test3 = new IntPtr(@int);
            }

            public IntPtr GetTest3()
            {
                return test3;
            }
        }

	    public struct TypicalService_Request_t
        {
	        public rosidl_generator_c__primitive_array_float64 test2;
	    }

	    public static void TestMethod()
	    {
	        var request = new TypicalService_Request_t();
	        request.test2.SetTest3(3);

	        Console.WriteLine("Init - " + request.GetType());

	        var type = request.GetType();
	        var publicItems = type.GetFields();
            var privateItems = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

	        var totalItems = new FieldInfo[publicItems.Length + privateItems.Length];
	        publicItems.CopyTo(totalItems, 0);
	        privateItems.CopyTo(totalItems, publicItems.Length);

	        foreach (var item in totalItems)
	        {
	            var itemType = item.FieldType;
                if (typeof(IntPtr) == itemType)
	            {
                    Console.WriteLine("Here1");
	            }

	            var itemPublicItems = itemType.GetFields();
	            var itemPrivateItems = itemType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

                var itemTotalItems = new FieldInfo[itemPublicItems.Length + itemPrivateItems.Length];
	            itemPublicItems.CopyTo(itemTotalItems, 0);
	            itemPrivateItems.CopyTo(itemTotalItems, itemPublicItems.Length);

	            foreach (var itemSecondLevel in itemTotalItems)
	            {
	                var itemTypeSecondLevel = itemSecondLevel.FieldType;
	                if (typeof(IntPtr) == itemTypeSecondLevel)
	                {
	                    Console.WriteLine("Here2");

	                    var avant = request.test2.GetTest3();

                        var itemValue = item.GetValue(request);

                        itemSecondLevel.SetValue(itemValue, IntPtr.Zero);

	                    object boxed = request;

                        item.SetValue(boxed, itemValue);

	                    request = boxed;
	                    
                        //itemSecondLevel.SetValue(@this.test2, IntPtr.Zero);
	                    var apres = request.test2.GetTest3();
                    }
                }

	            var test = request.test2.GetTest3();
                Console.WriteLine(test);
            }
        }*/
    }
}


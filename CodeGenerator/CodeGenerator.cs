using System;
using System.IO;
using System.Text;
using System.Reflection;
using ProtocolBuffers;
using System.Collections.Generic;

namespace ProtocolBuffers
{
	public class CodeGenerator
	{
		readonly Proto proto;
		readonly string ns;
		
		public CodeGenerator (Proto proto)
		{
			this.proto = proto;
			this.ns = proto.Options ["namespace"];
		}
		
		#region Path and Namespace generators

		private string FullPath (Message message, string path)
		{
			while (message.Parent != null && !(message.Parent is Proto)) {
				message = message.Parent;
				path = message.CSName + "." + path;
			}
			if (message.Options.ContainsKey ("namespace"))
				return message.Options ["namespace"] + "." + path;
			return ns + "." + path;
		}
		
		/// <summary>
		/// Prepend namespace to class name
		/// </summary>
		private string FullNS (Message message)
		{
			string path = message.CSName;
			return FullPath (message, path);
		}
		
		/// <summary>
		/// Generate full Interface path
		/// </summary>
		private string PropertyType (Message message)
		{
#if GENERATE_INTERFACE
			string path = "I" + message.CSName;
#else
			string path = message.CSName;
#endif
			return FullPath (message, path);
		}
		
		/// <summary>
		/// Generate full Interface path
		/// </summary>
		private string PropertyItemType (Field field)
		{
			switch (field.ProtoType) {
			case ProtoTypes.Message:
				return FullPath (field.ProtoTypeMessage, field.CSType);
			case ProtoTypes.Enum:
				string path = field.CSType;
				Message message = field.ProtoTypeEnum.Parent;
				if (message is Proto == false)
					path = message.CSName + "." + path;
				return FullPath (message, path);
			default:	
				return field.CSType;
			}
		}
		
		/// <summary>
		/// Generate full Interface path
		/// </summary>
		private string PropertyType (Field field)
		{
			if (field.Rule == Rules.Repeated)
				return "List<" + PropertyItemType (field) + ">";
			else
				return PropertyItemType (field);
		}
		
		private string FullClass (Field f)
		{
			IProtoType pt;
			if (f.ProtoType == ProtoTypes.Message)
				pt = f.ProtoTypeMessage;
			else if (f.ProtoType == ProtoTypes.Enum)
				pt = f.ProtoTypeEnum;
			else
				throw new InvalidOperationException ();
			
			string path = pt.CSName;
			while (true) {
				if (pt.Parent is Proto) {
					return GetNamespace ((Message)pt) + "." + path;
				}
				pt = pt.Parent;
				path = pt.CSName + "." + path;
			}
		}
		
		private string GetNamespace (Message m)
		{
			if (m.Options.ContainsKey ("namespace") == false)
				return ns;
			return m.Options ["namespace"];
		}
		
		#endregion
		
		/// <summary>
		/// Generate code for reading and writing protocol buffer messages
		/// </summary>
		public void Save (string csPath)
		{
			//Basic structures
			using (TextWriter codeWriter = new StreamWriter(csPath, false, Encoding.UTF8)) {
				codeWriter.WriteLine (@"//
//	You may customize this code as you like
//	Report bugs to: https://silentorbit.com/protobuf-csharpgen/
//
//	Generated by ProtocolBuffer
//	- a pure c# code generation implementation of protocol buffers
//

using System;
using System.Collections.Generic;
");
				
				foreach (Message m in proto.Messages) {
					codeWriter.WriteLine ("namespace " + GetNamespace (m));
					codeWriter.WriteLine ("{");
					codeWriter.WriteLine (Indent (1, GenerateClass (m)));
					codeWriter.WriteLine ("}");
				}
			}
			
			string ext = Path.GetExtension (csPath);
			string serializerPath = csPath.Substring (0, csPath.Length - ext.Length) + ".Serializer" + ext;
			
			//Code for Reading/Writing 
			using (TextWriter codeWriter = new StreamWriter(serializerPath, false, Encoding.UTF8)) {
				codeWriter.WriteLine (@"//
//	This is the backend code for reading and writing
//	Report bugs to: https://silentorbit.com/protobuf-csharpgen/
//
//	Generated by ProtocolBuffer
//	- a pure c# code generation implementation of protocol buffers
//

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ProtocolBuffers;");

				foreach (Message m in proto.Messages) {
					codeWriter.WriteLine ("namespace " + GetNamespace (m));
					codeWriter.WriteLine ("{");
					codeWriter.WriteLine (Indent (1, GenerateClassSerializer (m)));
					codeWriter.WriteLine ("}");
				}

				codeWriter.WriteLine (@"
namespace ProtocolBuffers
{
	public static partial class Serializer
	{");

				foreach (Message m in proto.Messages)
					codeWriter.WriteLine (Indent (2, GenerateGenericClassSerializer (m)));
					
				codeWriter.WriteLine (@"
	}
}");
			}
				
			string libPath = Path.Combine (Path.GetDirectoryName (csPath), "ProtocolParser.cs");
			using (TextWriter codeWriter = new StreamWriter(libPath, false, Encoding.UTF8)) {
				ReadCode (codeWriter, "ProtocolParser", true);
				ReadCode (codeWriter, "ProtocolParserFixed", false);
				ReadCode (codeWriter, "ProtocolParserKey", false);
				ReadCode (codeWriter, "ProtocolParserVarInt", false);
			}
		}
		
		/// <summary>
		/// Read c# code from sourcePath and write it on code without the initial using statements.
		/// </summary>
		private static void ReadCode (TextWriter code, string name, bool includeUsing)
		{
			code.WriteLine ("#region " + name);
			
			using (TextReader tr = new StreamReader(Assembly.GetExecutingAssembly ().GetManifestResourceStream (name), Encoding.UTF8)) {
				while (true) {
					string line = tr.ReadLine ();
					if (line == null)
						break;
					if (includeUsing == false && line.StartsWith ("using"))
						continue;
					
					code.WriteLine (line);
				}
			}
			code.WriteLine ("#endregion");
		}
		
#if GENERATE_INTERFACE
		string GenerateInterface (Message m)
		{
			string properties = "";
			foreach (Field f in m.Fields) {
				if (f.Deprecated)
					properties += "[Obsolete]\n";
				properties += PropertyType (f) + " " + f.Name + " { get; set; }\n";
			}
			
			string code = "";
			code += "public interface I" + m.CSName + "\n";
			code += "{\n";
			code += Indent (properties);
			code += "}\n";
			return code;
		}
#endif
		
		string GenerateClass (Message m)
		{
			//Enums
			string enums = "";
			foreach (MessageEnum me in m.Enums) {
				enums += "public enum " + me.CSName + "\n";
				enums += "{\n";
				foreach (var epair in me.Enums)
					enums += "	" + epair.Key + " = " + epair.Value + ",\n";
				enums += "}\n";
			}
			
			//Properties
			string properties = "";
			foreach (Field f in m.Fields) {
			#if GENERATE_BASE
				properties += f.Access + " virtual " + PropertyType (f) + " " + f.Name + " { get; set; }\n";
				#else
				properties += f.Access + " " + PropertyType (f) + " " + f.Name + " { get; set; }\n";
				#endif
			}
			
			//Constructor with default values
			string constructor = "";
			foreach (Field f in m.Fields) {
				if (f.Rule == Rules.Repeated)
					constructor += "	this." + f.Name + " = new List<" + PropertyItemType (f) + ">();\n";
				else if (f.Default != null) {
					if (f.ProtoType == ProtoTypes.Enum)
						constructor += "	this." + f.Name + " = " + FullClass (f) + "." + f.Default + ";\n";
					else
						constructor += "	this." + f.Name + " = " + f.Default + ";\n";
				} else if (f.Rule == Rules.Optional) {
					if (f.ProtoType == ProtoTypes.Enum) {
						//the default value is the first value listed in the enum's type definition
						foreach (var kvp in f.ProtoTypeEnum.Enums) {
							constructor += "	this." + f.Name + " = " + kvp.Key + ";\n";
							break;
						}
					}
					if (f.ProtoType == ProtoTypes.String) {
						constructor += "	this." + f.Name + " = \"\";\n";
					}
				}
			}
			if (constructor != "") {
			#if GENERATE_BASE
				constructor = "protected " + m.CSName + "Base()\n{\n" + constructor + "}\n";
			#else
				constructor = "public " + m.CSName + "()\n{\n" + constructor + "}\n";
			#endif
			
			}
			
			string code = "";

			#if GENERATE_BASE
			//Base class
			code += "public abstract class " + m.CSName + "Base\n";
			code += "{\n";
			code += Indent (properties);
			code += "\n";
			code += Indent (constructor);
			code += "	protected virtual void BeforeSerialize()\n";
			code += "	{\n";
			code += "	}\n";
			code += "\n";
			code += "	protected virtual void AfterDeserialize()\n";
			code += "	{\n";
			code += "	}\n";
			code += "}\n\n";
			#endif

			//Default class
			
#if GENERATE_INTERFACE
			code += "public partial class " + m.CSName + " : I" + m.CSName + "\n";
#else
			#if GENERATE_BASE
			code += "public partial class " + m.CSName + " : " + m.CSName + "Base\n";
			#else
			code += "public partial class " + m.CSName + "\n";
			#endif
#endif
			code += "{\n";
			if (enums.Length > 0) {
				code += Indent (enums);
				code += "\n";
			}
			#if !GENERATE_BASE
			code += Indent (properties);
			code += "\n";
			code += Indent (constructor);
			
			if (m.Options.ContainsKey ("triggers") == false) {
				code += "\n";
				code += "	protected void BeforeSerialize()\n";
				code += "	{\n";
				code += "	}\n";
				code += "\n";
				code += "	protected void AfterDeserialize()\n";
				code += "	{\n";
				code += "	}\n";
			}
			
			#endif
			foreach (Message sub in m.Messages) {
				code += "\n";
				code += Indent (GenerateClass (sub));
			}
			code += "}\n";
#if GENERATE_INTERFACE
			code += "\n";
			code += GenerateInterface (m);
#endif
			return code;
		}
		
		string GenerateClassSerializer (Message m)
		{
			string code = "";
			code += "public partial class " + m.CSName + "\n";
			code += "{\n";
			code += Indent (GenerateReader (m));
			code += "\n";
			code += Indent (GenerateWriter (m));
			foreach (Message sub in m.Messages) {
				code += "\n";
				code += Indent (GenerateClassSerializer (sub));
			}
			code += "}\n";
			code += "\n";
			return code;
		}
		
		string GenerateGenericClassSerializer (Message m)
		{
			string code = "";
			code += "\n";
			code += GenerateGenericReader (m);
			code += "\n";
			code += GenerateGenericWriter (m);
			code += "\n";
			foreach (Message sub in m.Messages) {
				code += "\n";
				code += GenerateGenericClassSerializer (sub);
			}
			return code;
		}
		
		#region Protocol Reader
		
		string GenerateReader (Message m)
		{
			string code = "";
			code += "public static " + m.CSName + " Deserialize(Stream stream)\n";
			code += "{\n";
			code += "	" + m.CSName + " instance = new " + m.CSName + "();\n";
			code += "	Deserialize(stream, instance);\n";
			code += "	return instance;\n";
			code += "}\n";
			code += "\n";
			code += "public static " + m.CSName + " Deserialize(byte[] buffer)\n";
			code += "{\n";
			code += "	using(MemoryStream ms = new MemoryStream(buffer))\n";
			code += "		return Deserialize(ms);\n";
			code += "}\n";
			code += "\n";
			code += "public static T Deserialize<T> (Stream stream) where T : " + PropertyType (m) + ", new()\n";
			code += "{\n";
			code += "	T instance = new T ();\n";
			code += "	Deserialize (stream, instance);\n";
			code += "	return instance;\n";
			code += "}\n";
			code += "\n";
			code += "public static T Deserialize<T> (byte[] buffer) where T : " + PropertyType (m) + ", new()\n";
			code += "{\n";
			code += "	T instance = new T ();\n";
			code += "	using (MemoryStream ms = new MemoryStream(buffer))\n";
			code += "		Deserialize (ms, instance);\n";
			code += "	return instance;\n";
			code += "}\n";
			code += "\n";
			
			code += "public static " + PropertyType (m) + " Deserialize(Stream stream, " + PropertyType (m) + " instance)\n";
			code += "{\n";
			foreach (Field f in m.Fields) {
				if (f.WireType == Wire.Fixed32 || f.WireType == Wire.Fixed64) {
					code += "	BinaryReader br = new BinaryReader (stream);";
					break;
				}
			}
			code += "	while (true)\n";
			code += "	{\n";
			code += "		ProtocolBuffers.Key key = null;\n";
			code += "		try {\n";
			code += "			key = ProtocolParser.ReadKey (stream);\n";
			code += "		} catch (InvalidDataException) {\n";
			code += "			break;\n";
			code += "		}\n";
			code += "\n";
			code += "		switch (key.Field) {\n";
			code += "		case 0:\n";
			code += "			throw new InvalidDataException(\"Invalid field id: 0, something went wrong in the stream\");\n";
			foreach (Field f in m.Fields) {
				code += "		case " + f.ID + ":\n";
				code += Indent (3, GenerateFieldReader (f)) + "\n";
				code += "			break;\n";
			}
			code += "		default:\n";
			code += "			ProtocolParser.SkipKey(stream, key);\n";
			code += "			break;\n";
			code += "		}\n";
			code += "	}\n";
			code += "	\n";
			if (m.Options.ContainsKey ("triggers") && m.Options ["triggers"] != "off")
				code += "	instance.AfterDeserialize();\n";
			code += "	return instance;\n";
			code += "}\n";
			code += "\n";
			code += "public static " + PropertyType (m) + " Read(byte[] buffer, " + PropertyType (m) + " instance)\n";
			code += "{\n";
			code += "	using (MemoryStream ms = new MemoryStream(buffer))\n";
			code += "		Deserialize (ms, instance);\n";
			code += "	return instance;\n";
			code += "}\n";

			return code;
		}

		string GenerateGenericReader (Message m)
		{
			string code = "";
			code += "public static " + PropertyType (m) + " Read (Stream stream, " + PropertyType (m) + " instance)\n";
			code += "{\n";
			code += "	return " + PropertyType (m) + ".Deserialize(stream, instance);\n";
			code += "}\n";
			code += "\n";
			code += "public static " + PropertyType (m) + " Read(byte[] buffer, " + PropertyType (m) + " instance)\n";
			code += "{\n";
			code += "	using (MemoryStream ms = new MemoryStream(buffer))\n";
			code += "		" + PropertyType (m) + ".Deserialize (ms, instance);\n";
			code += "	return instance;\n";
			code += "}\n";
			return code;
		}

		string GenerateFieldReader (Field f)
		{
			string code = "";
			if (f.Rule == Rules.Repeated) {
				if (f.Packed == true) {
					code += "using(MemoryStream ms" + f.ID + " = new MemoryStream(ProtocolParser.ReadBytes(stream)))\n";
					code += "{\n";
					code += "	while(true)\n";
					code += "	{\n";
					code += "		if(ms" + f.ID + ".Position == ms" + f.ID + ".Length)\n";
					code += "			break;\n";
					code += "		instance." + f.Name + ".Add(" + GenerateFieldTypeReader (f, "ms" + f.ID, "br", null) + ");\n";
					code += "	}\n";
					code += "}\n";
				} else {
					code += "instance." + f.Name + ".Add(" + GenerateFieldTypeReader (f, "stream", "br", null) + ");";
				}
			} else {			
				if (f.ProtoType == ProtoTypes.Message) {
					code += "if(instance." + f.Name + " == null)\n";
					code += "	instance." + f.Name + " = " + FullClass (f) + ".Deserialize(ProtocolParser.ReadBytes(stream));\n";
					code += "else\n";
					code += "	instance." + f.Name + " = " + GenerateFieldTypeReader (f, "stream", "br", "instance." + f.Name) + ";";
				} else
					code += "instance." + f.Name + " = " + GenerateFieldTypeReader (f, "stream", "br", "instance." + f.Name) + ";";
			}
			return code;
		}

		string GenerateFieldTypeReader (Field f, string stream, string binaryReader, string instance)
		{
			switch (f.ProtoType) {
			case ProtoTypes.Double:
				return "br.ReadDouble ()";
			case ProtoTypes.Float:
				return "br.ReadSingle ()";
			case ProtoTypes.Fixed32:
				return "br.ReadUInt32 ()";
			case ProtoTypes.Fixed64:
				return "br.ReadUInt64 ()";
			case ProtoTypes.Sfixed32:
				return "br.ReadInt32 ()";
			case ProtoTypes.Sfixed64:
				return "br.ReadInt64 ()";
			case ProtoTypes.Int32:
				return "(int)ProtocolParser.ReadUInt32(" + stream + ")";
			case ProtoTypes.Int64:
				return "(long)ProtocolParser.ReadUInt64(" + stream + ")";
			case ProtoTypes.Uint32:
				return "ProtocolParser.ReadUInt32(" + stream + ")";
			case ProtoTypes.Uint64:
				return "ProtocolParser.ReadUInt64(" + stream + ");";
			case ProtoTypes.Sint32:
				return "ProtocolParser.ReadSInt32(" + stream + ");";
			case ProtoTypes.Sint64:
				return "ProtocolParser.ReadSInt64(" + stream + ");";
			case ProtoTypes.Bool:
				return "ProtocolParser.ReadBool(" + stream + ")";
			case ProtoTypes.String:
				return "ProtocolParser.ReadString(" + stream + ")";
			case ProtoTypes.Bytes:
				return "ProtocolParser.ReadBytes(" + stream + ")";
			case ProtoTypes.Enum:
				return "(" + PropertyItemType (f) + ")ProtocolParser.ReadUInt32(" + stream + ")";
			case ProtoTypes.Message:				
				if (f.Rule == Rules.Repeated)
					return FullClass (f) + ".Deserialize(ProtocolParser.ReadBytes(" + stream + "))";
				else
					return "Serializer.Read(ProtocolParser.ReadBytes(" + stream + "), " + instance + ")";
			default:
				throw new NotImplementedException ();
			}
		}

		#endregion
		
		#region Protocol Writer
		
		
		/// <summary>
		/// Generates code for writing a class/message
		/// </summary>
		string GenerateWriter (Message m)
		{
			string code = "public static void Serialize(Stream stream, " + m.CSName + " instance)\n";
			code += "{\n";
			if (m.Options.ContainsKey ("triggers") && m.Options ["triggers"] != "off") {
				code += "	instance.BeforeSerialize();\n";
				code += "\n";
			}
			if (GenerateBinaryWriter (m))
				code += "	BinaryWriter bw = new BinaryWriter(stream);\n";
			
			foreach (Field f in m.Fields) {
				code += Indent (GenerateFieldWriter (m, f));
			}
			code += "}\n\n";
			
			code += "public static byte[] SerializeToBytes(" + m.CSName + " instance)\n";
			code += "{\n";
			code += "	using(MemoryStream ms = new MemoryStream())\n";
			code += "	{\n";
			code += "		Serialize(ms, instance);\n";
			code += "		return ms.ToArray();\n";
			code += "	}\n";
			code += "}\n";
			
			return code;
		}
		
		/// <summary>
		/// Generates code for writing a class/message
		/// </summary>
		string GenerateGenericWriter (Message m)
		{
			string code = "";
			code += "public static void Write(Stream stream, " + PropertyType (m) + " instance)\n";
			code += "{\n";
			code += "	" + PropertyType (m) + ".Serialize(stream, instance);\n";
			code += "}\n";
			return code;
		}
		
		/// <summary>
		/// Adds BinaryWriter only if it will be used
		/// </summary>
		static bool GenerateBinaryWriter (Message m)
		{
			foreach (Field f in m.Fields) {
				if (f.WireType == Wire.Fixed32 || f.WireType == Wire.Fixed64) {
					return true;
				}
			}
			return false;
		}
		
		/// <summary>
		/// Generates code for writing one field
		/// </summary>
		string GenerateFieldWriter (Message m, Field f)
		{
			string code = "";
			if (f.Rule == Rules.Repeated) {
				if (f.Packed == true) {
					
					string binaryWriter = "";
					switch (f.ProtoType) {
					case ProtoTypes.Double:
					case ProtoTypes.Float:
					case ProtoTypes.Fixed32:
					case ProtoTypes.Fixed64:
					case ProtoTypes.Sfixed32:
					case ProtoTypes.Sfixed64:
						binaryWriter = "\nBinaryWriter bw" + f.ID + " = new BinaryWriter(ms" + f.ID + ");";
						break;
					}
					
					code += "ProtocolParser.WriteKey(stream, new ProtocolBuffers.Key(" + f.ID + ", Wire." + f.WireType + "));\n";
					code += "using(MemoryStream ms" + f.ID + " = new MemoryStream())\n";
					code += "{	" + binaryWriter + "\n";
					code += "	foreach (" + PropertyItemType (f) + " i" + f.ID + " in instance." + f.Name + ")\n";
					code += "	{\n";
					code += "" + Indent (2, GenerateFieldTypeWriter (f, "ms" + f.ID, "bw" + f.ID, "i" + f.ID)) + "\n";
					code += "	}\n";
					code += "	ProtocolParser.WriteBytes(stream, ms" + f.ID + ".ToArray());\n";
					code += "}\n";
					return code;
				} else {
					code += "foreach (" + PropertyItemType (f) + " i" + f.ID + " in instance." + f.Name + ")\n";
					code += "{\n";
					code += "	ProtocolParser.WriteKey(stream, new ProtocolBuffers.Key(" + f.ID + ", Wire." + f.WireType + "));\n";
					code += "" + Indent (1, GenerateFieldTypeWriter (f, "stream", "bw", "i" + f.ID)) + "\n";
					code += "}\n";
					return code;
				}
			} else if (f.Rule == Rules.Optional) {			
				switch (f.ProtoType) {
				case ProtoTypes.String:
				case ProtoTypes.Message:
				case ProtoTypes.Bytes:
					code += "if(instance." + f.Name + " != null)\n";
					code += "{\n";
					code += "	ProtocolParser.WriteKey(stream, new ProtocolBuffers.Key(" + f.ID + ", Wire." + f.WireType + "));\n";
					code += Indent (GenerateFieldTypeWriter (f, "stream", "bw", "instance." + f.Name));
					code += "}\n";
					return code;
				case ProtoTypes.Enum:
					code += "if(instance." + f.Name + " != " + PropertyItemType (f) + "." + f.Default + ")\n";
					code += "{\n";
					code += "	ProtocolParser.WriteKey(stream, new ProtocolBuffers.Key(" + f.ID + ", Wire." + f.WireType + "));\n";
					code += Indent (GenerateFieldTypeWriter (f, "stream", "bw", "instance." + f.Name));
					code += "}\n";
					return code;
				default:
					code += "ProtocolParser.WriteKey(stream, new ProtocolBuffers.Key(" + f.ID + ", Wire." + f.WireType + "));\n";
					code += GenerateFieldTypeWriter (f, "stream", "bw", "instance." + f.Name);
					return code;
				}
			} else if (f.Rule == Rules.Required) {			
				switch (f.ProtoType) {
				case ProtoTypes.String:
				case ProtoTypes.Message:
				case ProtoTypes.Bytes:
					code += "if(instance." + f.Name + " == null)\n";
					code += "	throw new ArgumentNullException(\"" + f.Name + "\", \"Required by proto specification.\");\n";
					break;
				}
				code += "ProtocolParser.WriteKey(stream, new ProtocolBuffers.Key(" + f.ID + ", Wire." + f.WireType + "));\n";
				code += GenerateFieldTypeWriter (f, "stream", "bw", "instance." + f.Name);
				return code;
			}			
			throw new NotImplementedException ("Unknown rule: " + f.Rule);
		}
					
		string GenerateFieldTypeWriter (Field f, string stream, string binaryWriter, string instance)
		{
			switch (f.ProtoType) {
			case ProtoTypes.Double:
			case ProtoTypes.Float:
			case ProtoTypes.Fixed32:
			case ProtoTypes.Fixed64:
			case ProtoTypes.Sfixed32:
			case ProtoTypes.Sfixed64:
				return binaryWriter + ".Write(" + instance + ");\n";
			case ProtoTypes.Int32:
				return "ProtocolParser.WriteUInt32(" + stream + ", (uint)" + instance + ");\n";
			case ProtoTypes.Int64:
				return "ProtocolParser.WriteUInt64(" + stream + ", (ulong)" + instance + ");\n";
			case ProtoTypes.Uint32:
				return "ProtocolParser.WriteUInt32(" + stream + ", " + instance + ");\n";
			case ProtoTypes.Uint64:
				return "ProtocolParser.WriteUInt64(" + stream + ", " + instance + ");\n";
			case ProtoTypes.Sint32:
				return "ProtocolParser.WriteSInt32(" + stream + ", " + instance + ");\n";
			case ProtoTypes.Sint64:
				return "ProtocolParser.WriteSInt64(" + stream + ", " + instance + ");\n";
			case ProtoTypes.Bool:
				return "ProtocolParser.WriteBool(" + stream + ", " + instance + ");\n";
			case ProtoTypes.String:
				return "ProtocolParser.WriteString(" + stream + ", " + instance + ");\n";
			case ProtoTypes.Bytes:
				return "ProtocolParser.WriteBytes(" + stream + ", " + instance + ");\n";
			case ProtoTypes.Enum:
				return "ProtocolParser.WriteUInt32(" + stream + ", (uint)" + instance + ");\n";
			case ProtoTypes.Message:				
				string code = "";
				code += "using(MemoryStream ms" + f.ID + " = new MemoryStream())\n";
				code += "{\n";
				code += "	" + FullClass (f) + ".Serialize(ms" + f.ID + ", " + instance + ");\n";
				code += "	ProtocolParser.WriteBytes(" + stream + ", ms" + f.ID + ".ToArray());\n";
				code += "}\n";
				return code;
			default:
				throw new NotImplementedException ();
			}
		}
		
		#endregion
	
		/// <summary>
		/// Indent all lines in the code string with one tab
		/// </summary>
		private static string Indent (string code)
		{
			return Indent (1, code);
		}
		
		/// <summary>
		/// Indent all lines in the code string with given number of tabs
		/// </summary>
		private static string Indent (int tabs, string code)
		{
			string sep = "\n";
			for (int n = 0; n < tabs; n++)
				sep += "\t";
			code = sep + string.Join (sep, code.Split ('\n'));
			return code.Substring (1).TrimEnd ('\t');			
		}
		
	}
}


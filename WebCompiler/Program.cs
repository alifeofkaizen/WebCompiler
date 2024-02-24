﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebCompiler.Compile;
using WebCompiler.Configuration;

namespace WebCompiler
{
	public static class Extension
	{
		public static int IndexOfAny<T>(this IEnumerable<T> enumerable, params T[] values)
		{
			if (!values.Any())
			{
				return -1;
			}
			foreach (var (element, index) in enumerable.Select((e, i) => (e, i)))
			{
				if (values.Contains(element))
				{
					return index;
				}
			}
			return -1;
		}
		public static bool ContainsOption(this List<string> args, string short_option, string long_option, [NotNullWhen(true)] out string? value)
		{
			value = null;
			if (args.Contains(short_option) || args.Contains(long_option))
			{
				var arg_index = args.IndexOfAny(short_option, long_option) + 1;
				if (arg_index >= args.Count)
				{
					Console.Error.WriteLine($"Argument missing for option {short_option}.");
					return false;
				}
				value = args[arg_index];
				return true;
			}
			return false;
		}
	}
	internal static class Program
	{
		private static readonly JsonSerializerOptions json_serializer_options = CreateDefaultOptions();
		private static JsonSerializerOptions CreateDefaultOptions()
		{
			var options = new JsonSerializerOptions
			{
				AllowTrailingCommas = true,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
				PropertyNameCaseInsensitive = true,
				WriteIndented = true,
			};
			options.Converters.Add(new JsonStringEnumConverter());
			return options;
		}

		private static void ShowHelp(bool show_file_help = false, bool show_output_help = false, bool show_config_help = false)
		{
			Console.WriteLine(
@"
Excubo.WebCompiler

Usage:
  webcompiler file1 file2 ... [options]
  webcompiler [options]

Options:
  -c|--config <conf.json>          Specify a configuration file for compilation.
  -d|--defaults [conf.json]        Write a default configuration file (file name is webcompilerconfiguration.json, if none is specified).
  -f|--files <files.conf>          Specify a list of files that should be compiled.
  -h|--help                        Show command line help.
  -m|--minify [disable/enable]     Enable/disable minification (default: enabled), ignored if configuration file is provided.
  -o|--output-dir <path/to/dir>    Specify the output directory, ignored if configuration file is provided.
  -p|--preserve [disable/enable]   Enable/disable whether to preserve intermediate files (default: enabled).
  -r|--recursive                   Recursively search folders for compilable files (only if any of the provided arguments is a folder).
  -z|--zip [disable/enable]        Enable/disable gzip (default: enabled), ignored if configuration file is provided.
  -a|--autoprefix [disable/enable] Enable/disable autoprefixing (default: enabled), ignored if configuration file is provided.
".Trim()
			);
			if (show_file_help)
			{
				Console.WriteLine();
				Console.WriteLine(
@"
File format to specify list of files (-f|--files): one file per line
  path/to/file.scss
  path/to/other/file.scss
".Trim()
				);
			}
			if (show_output_help)
			{
				Console.WriteLine();
				Console.WriteLine(
@"
Specifying the output directory has the following effect:
Suppose the specified output directory is
  wwwroot
and the input files are
  path/to/file.scss
  path/to/other/file.scss
  path/code.js

All these files share the common prefix ""path/"", which is ignored.
The output files will therefore be
  wwwroot/to/file.min.css
  wwwroot/to/other/file.min.css
  wwwroot/code.min.js
".Trim()
				);
			}
			if (show_config_help)
			{
				Console.WriteLine();
				Console.WriteLine(
@"
File format to specify compiler configuration (-c|--config):
    {
        ""compilers"": {
            ""sass"": {
                ""includePath"": """",
                ""indentType"": ""space"",
                ""indentWidth"": 2,
                ""outputStyle"": ""nested"",
                ""Precision"": 5,
                ""relativeUrls"": true,
                ""sourceMapRoot"": """",
                ""lineFeed"": """",
                ""sourceMap"": false
            },
        },
        ""minifiers"": {
            ""css"": {
                ""enabled"": true,
                ""termSemicolons"": true,
                ""gzip"": true
            },
            ""javascript"": {
                ""enabled"": true,
                ""termSemicolons"": true,
                ""gzip"": false
            }
        },
        ""autoprefix"": {
            ""enabled"": true,
            ""processingOptions"": {
                ""browsers"": [
                    ""last 4 versions""
                ],
                ""cascade"": true,
                ""add"": true,
                ""remove"": true,
                ""supports"": true,
                ""flexbox"": ""All"",
                ""grid"": ""None"",
                ""ignoreUnknownVersions"": false,
                ""stats"": """",
                ""sourceMap"": true,
                ""inlineSourceMap"": false,
                ""sourceMapIncludeContents"": false,
                ""omitSourceMapUrl"": false
            }
        }
    }
".Trim()
				);
			}
		}
		public static int Main(params string[] args)
		{
			if (!args.Any() || args.Contains("-h") || args.Contains("--help"))
			{
				ShowHelp(show_file_help: args.Contains("-f") || args.Contains("--files"),
						 show_output_help: args.Contains("-o") || args.Contains("--output-dir"),
						 show_config_help: args.Contains("-c") || args.Contains("--config"));
				return 0;
			}
			if (args.Contains("-d") || args.Contains("--defaults"))
			{
				CreateDefaultConfig(args.ToList());
				return 0;
			}
			var config = GetConfig(args.ToList());
			if (config == null)
			{
				return 1;
			}
			var file_arguments = GetFileArguments(args.ToList()).ToList();
			if (!file_arguments.Any())
			{
				Console.Error.WriteLine("No file or folder specified.");
				return 1;
			}
			var recurse = args.Contains("-r") || args.Contains("--recursive");
			var base_path = GetCommonBase(file_arguments);
			var compilers = new Compilers(config, base_path);
			foreach (var item in file_arguments)
			{
				if (Directory.Exists(item))
				{
					if (!recurse)
					{
						Console.WriteLine($"{item} is a directory, but option -r is not used. Ignoring {item} and all items in it.");
					}
					foreach (var file in Helpers.FileFolderHelpers.Recurse(item, config.CompilerSettings.Ignore))
					{
						var result = compilers.TryCompile(file);
						if (result.Errors != null)
						{
							PrintErrors(result.Errors);
							if (result.Errors.Any(e => !e.IsWarning))
							{
								return 1;
							}
						}
					}
				}
				else if (File.Exists(item))
				{
					// Determine whether the file, even though it is explicitly specified in the CLI, is subject to exclusion in config.
					var parentDir = Path.GetDirectoryName(item);
					if (parentDir != null)
					{
						if (!Helpers.FileFolderHelpers.Test(parentDir, config.CompilerSettings.Ignore, Path.GetFullPath(item)))
						{
							// The item is ignored, as recursing the parent directory.
							continue;
						}
					}
					var result = compilers.TryCompile(item);
					if (result.Errors != null)
					{
						PrintErrors(result.Errors);
						if (result.Errors.Any(e => !e.IsWarning))
						{
							return 1;
						}
					}
				}
				else
				{
					Console.Error.WriteLine($"{item} does not exist");
				}
			}
			return 0;
		}

		private static string GetCommonBase(List<string> paths)
		{
			paths = paths.Select(p => Path.GetFullPath(p)).ToList();
			return paths
				.Aggregate(File.Exists(paths.First()) ? Path.GetDirectoryName(paths.First()) : paths.First(), (f, s) =>
			{
				if (File.Exists(f))
				{
					f = Path.GetDirectoryName(f)!;
				}
				if (File.Exists(s))
				{
					s = Path.GetDirectoryName(s)!;
				}
				while (!s.StartsWith(f))
				{
					f = Path.GetDirectoryName(f)!;
				}
				return f;
			});
		}
		private static void PrintErrors(List<CompilerError> errors)
		{
			foreach (var error in errors)
			{
				Console.Error.WriteLine(error.Message);
				if (error.FileName != null)
				{
					Console.Error.WriteLine($"-- in file {error.FileName} L{error.LineNumber}:{error.ColumnNumber}");
				}
			}
		}

		private static readonly List<string> other_options = new List<string>
		{
			"-d", "--defaults",
			"-h", "--help"
		};
		private static readonly List<string> options_with_arguments = new List<string>
		{
			"-c", "--config",
			"-f", "--files",
			"-m", "--minify",
			"-o", "--output-dir",
			"-p", "--preserve",
			"-z", "--zip",
			"-a", "--autoprefix"
		};
		private static readonly List<string> options_without_arguments = new List<string>
		{
			"-r", "--recursive"
		};
		private static IEnumerable<string> GetFileArguments(List<string> args)
		{
			/*
  -c|--config <conf.json>          Specify a configuration file for compilation.
  -d|--defaults [conf.json]        Write a default configuration file (file name is webcompilerconfiguration.json, if none is specified).
  -f|--files <files.conf>          Specify a list of files that should be compiled.
  -h|--help                        Show command line help.
  -m|--minify [disable/enable]     Enable/disable minification (default: enabled), ignored if configuration file is provided.
  -o|--output-dir <path/to/dir>    Specify the output directory, ignored if configuration file is provided.
  -p|--preserve [disable/enable]   Enable/disable whether to preserve intermediate files (default: enabled).
  -r|--recursive                   Recursively search folders for compilable files (only if any of the provided arguments is a folder).
  -z|--zip [disable/enable]        Enable/disable gzip (default: enabled), ignored if configuration file is provided.
  -a|--autoprefix [disable/enable] Enable/disable autoprefixing (default: enabled), ignored if configuration file is provided.
             */
			if (other_options.Intersect(args).Any())
			{
				throw new ArgumentException("Invalid handling of arguments (program flow went past helper actions). Please report as a bug.");
			}
			for (var i = 0; i < args.Count; ++i)
			{
				if (options_with_arguments.Contains(args[i]))
				{
					// ignore this item and the next
					++i;
					continue;
				}
				if (options_without_arguments.Contains(args[i]))
				{
					// ignore this item
					continue;
				}
				// this seems to be a file or folder.
				yield return args[i];
			}
			if (args.Contains("-f") || args.Contains("--files"))
			{
				var arg_index = args.IndexOfAny("-f", "--files") + 1;
				if (arg_index >= args.Count)
				{
					Console.Error.WriteLine("Argument missing for option -f. Did you mean to add a list of files?");
				}
				var file = args[arg_index];
				if (!File.Exists(file))
				{
					Console.Error.WriteLine($"File {file} not found");
					yield break;
				}
				foreach (var item in File.ReadAllLines(file, Compiler.Encoding))
				{
					yield return item;
				}
			}
		}

		private static Config? GetConfig(List<string> args)
		{
			Config? config = null;
			if (args.Contains("-c") || args.Contains("--config"))
			{
				try
				{
					config = GetConfigFromFile(args);
				}
				catch
				{
					return null;
				}
			}
			config ??= new Config();
			if (IsMinificationDisabled(args))
			{
				config.Minifiers.Enabled = false;
			}
			else if (IsMinificationEnabled(args))
			{
				config.Minifiers.Enabled = true;
			}
			if (IsCompressionDisabled(args))
			{
				config.Minifiers.GZip = false;
			}
			else if (IsCompressionEnabled(args))
			{
				config.Minifiers.GZip = true;
			}
			if (IsPreservationDisabled(args))
			{
				config.Output.Preserve = false;
			}
			else if (IsPreservationEnabled(args))
			{
				config.Output.Preserve = true;
			}
			if (IsAutoprefixDisabled(args))
			{
				config.Autoprefix.Enabled = false;
			}
			else if (IsAutoprefixEnabled(args))
			{
				config.Autoprefix.Enabled = true;
			}
			if (args.ContainsOption("-o", "--output-dir", out var argument))
			{
				config.Output.Directory = argument;
			}
			return config;
		}
		private static bool IsOptionDisabled(List<string> args, string short_option, string long_option, string feature)
		{
			if (args.Contains(short_option) || args.Contains(long_option))
			{
				var arg_index = args.IndexOfAny(short_option, long_option) + 1;
				if (arg_index >= args.Count)
				{
					Console.Error.WriteLine($"Argument missing for option {short_option}. Did you mean to disable {feature}?");
					return false;
				}
				return args[arg_index].StartsWith("d", StringComparison.InvariantCultureIgnoreCase);
			}
			return false;
		}
		private static bool IsOptionEnabled(List<string> args, string short_option, string long_option, string feature)
		{
			if (args.Contains(short_option) || args.Contains(long_option))
			{
				var arg_index = args.IndexOfAny(short_option, long_option) + 1;
				if (arg_index >= args.Count)
				{
					Console.Error.WriteLine($"Argument missing for option {short_option}. Did you mean to enable {feature}?");
					return false;
				}
				return args[arg_index].StartsWith("e", StringComparison.InvariantCultureIgnoreCase);
			}
			return false;
		}
		private static bool IsCompressionDisabled(List<string> args)
		{
			return IsOptionDisabled(args, "-z", "--zip", "compression");
		}
		private static bool IsCompressionEnabled(List<string> args)
		{
			return IsOptionEnabled(args, "-z", "--zip", "compression");
		}
		private static bool IsMinificationDisabled(List<string> args)
		{
			return IsOptionDisabled(args, "-m", "--minify", "minification");
		}
		private static bool IsMinificationEnabled(List<string> args)
		{
			return IsOptionEnabled(args, "-m", "--minify", "minification");
		}
		private static bool IsPreservationDisabled(List<string> args)
		{
			return IsOptionDisabled(args, "-p", "--preserve", "preservation of intermediate files");
		}
		private static bool IsPreservationEnabled(List<string> args)
		{
			return IsOptionEnabled(args, "-p", "--preserve", "preservation of intermediate files");
		}
		private static bool IsAutoprefixDisabled(List<string> args)
		{
			return IsOptionDisabled(args, "-a", "--autoprefix", "autoprefix");
		}
		private static bool IsAutoprefixEnabled(List<string> args)
		{
			return IsOptionEnabled(args, "-a", "--autoprefix", "autoprefix");
		}
		private static Config? GetConfigFromFile(List<string> args)
		{
			try
			{
				var default_index = args.IndexOfAny("-c", "--config");
				var file_arg_index = default_index + 1;
				if (file_arg_index < args.Count && !args[file_arg_index].StartsWith("-"))
				{
					var config_path = args[file_arg_index];
					return JsonSerializer.Deserialize<Config>(File.ReadAllText(config_path, Compiler.Encoding), json_serializer_options);
				}
				else
				{
					Console.Error.WriteLine("Error reading configuration from file: no file specified");
					throw new ArgumentException("Missing configuration file.");
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Error reading configuration from file: {e.Message}");
				throw;
			}
		}
		private static void CreateDefaultConfig(List<string> args)
		{
			var default_index = args.IndexOfAny("-d", "--defaults");
			var file_arg_index = default_index + 1;
			var config_path = "webcompilerconfiguration.json";
			if (file_arg_index < args.Count && !args[file_arg_index].StartsWith("-"))
			{
				config_path = args[file_arg_index];
			}
			File.WriteAllText(config_path, JsonSerializer.Serialize(new Config(), json_serializer_options));
		}
	}
}
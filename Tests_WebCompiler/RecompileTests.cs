﻿using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WebCompiler.Compile;
using WebCompiler.Configuration.Settings;

namespace Tests_WebCompiler
{
    public class RecompileTests : TestsBase
    {
        [SetUp]
        public void CreatePipeline()
        {
            pipeline = (file) => new CompilationStep(file).With(new SassCompiler(new SassSettings())).Then(new CssMinifier(new CssMinifySettings { TermSemicolons = false }));
            input = "../../../TestCases/Scss/test.scss";
            output_files = new List<string> { "../../../TestCases/Scss/test.css", "../../../TestCases/Scss/test.min.css" };
            expected_output = "../../../TestCases/MinCss/test.min.css";
            DeleteTemporaryFiles();
        }
        [Test]
        public async Task CallTest()
        {
            var timestamp = ProcessFile();
            await Task.Delay(100); // create a delay, because if things happen fast enough, the accuracy of the file timestamp is too low to detect the change in file
            File.Copy(input, input + ".bak");
            File.AppendAllText(input, "\n.new-rule { color: black; }");
            await Task.Delay(100); // create a delay, because if things happen fast enough, the accuracy of the file timestamp is too low to detect the change in file
            var new_timestamp = ProcessFile();
            File.Move(input + ".bak", input, overwrite: true);
            Assert.That(timestamp, Is.Not.EqualTo(new_timestamp), "Compiling a second time should alter the file, since there is an actual change for once!");
        }
        [Test]
        public async Task CallNeedsCompileSubDirTest()
        {
            var timestamp = ProcessFile();
            await Task.Delay(100); // create a delay, because if things happen fast enough, the accuracy of the file timestamp is too low to detect the change in file
            var input_path = new FileInfo(input).DirectoryName ?? string.Empty;
            // update the scss source file in a sub directory.
            var import_file = Path.Combine(input_path, "sub", "_bar.scss");
            File.Copy(import_file, import_file + ".bak", overwrite: true);
            File.AppendAllText(import_file, "\n.new-rule { color: black; }");
            await Task.Delay(100); // create a delay, because if things happen fast enough, the accuracy of the file timestamp is too low to detect the change in file
            var new_timestamp = ProcessFile();
            File.Move(import_file + ".bak", import_file, overwrite: true);
            Assert.That(timestamp, Is.Not.EqualTo(new_timestamp), "Compiling a second time should alter the file, since there is an actual change for once!");
        }
    }
}
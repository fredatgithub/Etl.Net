﻿using System;
using System.Threading.Tasks;
using Paillave.Etl.Core;
using Paillave.Etl.FileSystem;
using Paillave.Etl.Zip;
using Paillave.Etl.TextFile;
using Paillave.Etl.SqlServer;
using System.Data.SqlClient;
using Paillave.Etl.ExecutionToolkit;
using System.Linq;

namespace SimpleTutorial
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var processRunner = StreamProcessRunner.Create<string>(DefineProcess4);
            var structure = processRunner.GetDefinitionStructure();
            // System.IO.File.WriteAllText(
            //     "execplan.json",
            //     Newtonsoft.Json.JsonConvert.SerializeObject(structure, Newtonsoft.Json.Formatting.Indented)
            // );
            // var structure = processRunner.GetDefinitionStructure();
            // structure.OpenEstimatedExecutionPlan();
            using (var cnx = new SqlConnection(args[1]))
            {
                cnx.Open();
                var executionOptions = new ExecutionOptions<string>
                {
                    Resolver = new SimpleDependencyResolver().Register(cnx),
                };
                var res = await processRunner.ExecuteAsync(args[0], executionOptions);

                // System.IO.File.WriteAllText(
                //     "execplan.json",
                //     Newtonsoft.Json.JsonConvert.SerializeObject(res.StreamStatisticCounters, Newtonsoft.Json.Formatting.Indented)
                // );
                // res.OpenActualExecutionPlan();

                Console.Write(res.Failed ? "Failed" : "Succeeded");
                if (res.Failed)
                    Console.Write($"{res.ErrorTraceEvent.NodeName}({res.ErrorTraceEvent.NodeTypeName}):{res.ErrorTraceEvent.Content.Message}");
            }
        }
        private static void DefineProcess(ISingleStream<string> contextStream)
        {
            contextStream
              .CrossApplyFolderFiles("list all required files", "*.zip", true)
              .CrossApplyZipFiles("extract files from zip", "*.csv")
              .CrossApplyTextFile("parse file", FlatFileDefinition.Create(i => new Person
              {
                  Email = i.ToColumn("email"),
                  FirstName = i.ToColumn("first name"),
                  LastName = i.ToColumn("last name"),
                  DateOfBirth = i.ToDateColumn("date of birth", "yyyy-MM-dd"),
                  Reputation = i.ToNumberColumn<int?>("reputation", ".")
              }).IsColumnSeparated(','))
              .Distinct("exclude duplicates based on the Email", i => i.Email)
              .SqlServerSave("upsert using Email as key and ignore the Id", "dbo.Person", p => p.Email, p => p.Id)
              .Select("define row to report", i => new { i.Email, i.Id })
              .ToTextFileValue("write summary to file", "report.csv", FlatFileDefinition.Create(i => new
              {
                  Email = i.ToColumn("Email"),
                  Id = i.ToNumberColumn<int>("new or existing Id", ".")
              }).IsColumnSeparated(','))
              .WriteToFile("save log file", i => i.Name);
        }
        private static void DefineProcess2(ISingleStream<string> stream)
        {
            var connectionParameters = new Paillave.Etl.Ftp.FtpAdapterConnectionParameters
            {
                Server = "my.ftp.server",
                Login = "my.login",
                Password = "P@SSW0RD",
            };
            var providerParameters = new Paillave.Etl.Ftp.FtpAdapterProviderParameters
            {
                SubFolder = "filesToPick"
            };
            stream.Select("create file", _ => FileValueWriter
                    .Create("fileExport.csv")
                    .Write("Here is the content of the file"))
                .WriteToFile("write to folder", i => i.Name);
            stream
                .CrossApply("azeazea", new Paillave.Etl.Ftp.FtpFileValueProvider("SRC", "Misc Source files", "files from ftp", connectionParameters, providerParameters))
                .Do("print file name to console", i => Console.WriteLine(i.Name));
        }
        private static void DefineProcess3(ISingleStream<string> stream)
        {
            var streamOfRows = stream
                .CrossApply("produce a list of dummy values for ", _ => Enumerable.Range(0, 100).Select(idx => new { Index = idx, Name = $"Index {idx}", CategoryId = idx % 3 }));

            var fileStream = streamOfRows
                .ToList("aggregate all rows")
                .Select("create file", rows => FileValueWriter
                    .Create("fileExport.txt")
                    .WriteLine("this content has no specific format")
                    .Write(String.Join(", ", rows.Select(row => row.Name).ToList())));

            fileStream.WriteToFile("write to folder", i => i.Name);
        }
        private static void DefineProcess6(ISingleStream<string> stream)
        {
            var streamOfRows = stream
                .CrossApply("produce a list of dummy values for ", _ => Enumerable.Range(0, 100).Select(idx => new { Index = idx, Name = $"Index {idx}", CategoryId = idx % 3 }));

            var fileStream = streamOfRows
                .Select("create row to save", i => new { i.Index, i.Name })
                .ToTextFileValue("save into csv file", $"fileExport.csv", FlatFileDefinition.Create(i => new
                {
                    Index = i.ToNumberColumn<int>("Idx", "."),
                    Name = i.ToColumn("Title")
                }).IsColumnSeparated(','));

            fileStream.WriteToFile("write to folder", i => i.Name);
        }
        private static void DefineProcess4(ISingleStream<string> stream)
        {
            stream
                .CrossApply("produce a list of dummy values for ", _ => Enumerable.Range(0, 100).Select(idx => new { Index = idx, Name = $"Index {idx}", CategoryId = idx % 3 }))
                .GroupBy("process per group", i => i.CategoryId, (subStream, firstRow) => subStream
                    .ToList("aggregate all rows")
                    .Select("create file", rows => FileValueWriter
                        .Create($"fileExport{firstRow.CategoryId}.txt")
                        .WriteLine($"here is the list of indexes in the category {firstRow.CategoryId}")
                        .Write(String.Join(", ", rows.Select(row => row.Name).ToList()))))
                .WriteToFile("write to folder", i => i.Name);
        }
        private static void DefineProcess7(ISingleStream<string> stream)
        {
            stream
                .CrossApply("produce a list of dummy values for ", _ => Enumerable.Range(0, 100).Select(idx => new { Index = idx, Name = $"Index {idx}", CategoryId = idx % 3 }))
                .GroupBy("process per group", i => i.CategoryId)
                .Select("create file", rows => FileValueWriter
                    .Create($"otherFileExport{rows.FirstValue.CategoryId}.txt")
                    .WriteLine($"here is the list of indexes in the category {rows.FirstValue.CategoryId}")
                    .Write(String.Join(", ", rows.Aggregation.Select(row => row.Name).ToList())))
                .WriteToFile("write to folder", i => i.Name);
        }
        private static void DefineProcess5(ISingleStream<string> stream)
        {
            stream
                .CrossApply("produce a list of dummy values for ", _ => Enumerable.Range(0, 100).Select(idx => new { Index = idx, Name = $"Index {idx}", CategoryId = idx % 3 }))
                .GroupBy("process per group", i => i.CategoryId, (subStream, firstRow) => subStream
                    .Select("create row to save", i => new { i.Index, i.Name })
                    .ToTextFileValue("save into csv file", $"fileExport{firstRow?.CategoryId}.csv", FlatFileDefinition.Create(i => new
                    {
                        Index = i.ToNumberColumn<int>("Idx", "."),
                        Name = i.ToColumn("Title")
                    })))
                .WriteToFile("write to folder", i => i.Name);
        }
        private class Person
        {
            public int Id { get; set; }
            public string Email { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public DateTime DateOfBirth { get; set; }
            public int? Reputation { get; set; }
        }
    }
}

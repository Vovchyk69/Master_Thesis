﻿using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Moss.Client;
using Moss.Report.Client;
using OpenAI.GPT3;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using PlagiarismChecker.AmazonS3;
using PlagiarismChecker.ANTLR;
using PlagiarismChecker.Grammars;
using PlagiarismChecker.POCO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PlagiarismChecker.Visitor;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace PlagiarismChecker.Controllers
{
    [ApiController]
    [Route("api/checker")]
    public class PlagiarsimDetectionController : ControllerBase
    {
        private readonly ILogger<PlagiarsimDetectionController> _loggers;

        private readonly IAwsConfiguration _configuration;
        private readonly IAwsStorage _awsStorage;

        public PlagiarsimDetectionController(IAwsConfiguration configuration, ILogger<PlagiarsimDetectionController> logger)
        {
            _loggers = logger;
            _configuration = configuration;
            _awsStorage = new AwsStorage(
                _configuration.AwsAccessKey,
                _configuration.AwsSecretAccessKey,
                _configuration.Region,
                _configuration.BucketName);
        }

        [HttpPost]
        [Route("chat-gpt")]
        public async Task<ChatGptResponse> Get(string promt)
        {
            if (string.IsNullOrEmpty(promt))
                return new ChatGptResponse { Error = new ChatGptResponseError { Message = "Empty promt" } };

            var gpt = new OpenAIService(new OpenAiOptions() { ApiKey = "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" });
            var completionResult = await gpt.Completions.CreateCompletion(new CompletionCreateRequest()
            {
                Prompt = promt,
                Model = "model-detect-v2",
                Temperature = 1,
                MaxTokens = 100,
                Stream = false,
                Stop = "\n",
                N = 1,
                LogProbs = 5,
            });

            return completionResult.Successful 
                ? new ChatGptResponse { Choices = completionResult.Choices.Select(x => x.Text) } 
                : new ChatGptResponse { Error = new ChatGptResponseError { Message = completionResult.Error.Message } };
             
         }

        [HttpGet]
        [Route("moss")]
        public IActionResult GetMeasureOfSimilarity(string promt)
        {
            return Ok();
        }

        [HttpPost]
        [Route("moss-report")]
        public async Task<MossReportRow[]> GetMeasureOfSimilarityReport(FilesToCompare files)
        {
            using var clientHandler = new HttpClientHandler();
            using var httpClient = new HttpClient(clientHandler);
            var reportClient = new MossReportClient(httpClient);

            var userid = 987654321;
            var client = new MossClient(userid, new MossClientOptions() { Language = MossLanguage.Csharp });

            var document1 = await _awsStorage.DownloadFileAsync(files.FileNames[0]);
            var document2 = await _awsStorage.DownloadFileAsync(files.FileNames[1]);

            var test = Encoding.UTF8.GetBytes(document1);
            client.AddFile(Encoding.UTF8.GetBytes(document1), fileDisplayName: "File 1");
            client.AddFile(Encoding.UTF8.GetBytes(document2), fileDisplayName: "File 2");

            var url = client.Submit();
            var report = await reportClient.GetReport(url).ConfigureAwait(false);
            return report;
        }

        [HttpPost]
        [Route("gpt-zero")]
        public async Task<object> Similarity(Documents document)
        {
            using var clientHandler = new HttpClientHandler();
            using var httpClient = new HttpClient(clientHandler);
            httpClient.DefaultRequestHeaders.Add("Cookie", "AMP_MKTG_8f1ede8e9c=JTdCJTdE; accessToken=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOiJhdXRoZW50aWNhdGVkIiwiZXhwIjoxNjc5MDA0MDExLCJzdWIiOiI2ZGJhMDgxOC0wN2Y0LTQyNmQtYTkzOS1hOTFlZjZiNmJlODgiLCJlbWFpbCI6InZvdmNoeWtoYWxhbWFoYUBnbWFpbC5jb20iLCJwaG9uZSI6IiIsImFwcF9tZXRhZGF0YSI6eyJwcm92aWRlciI6Imdvb2dsZSIsInByb3ZpZGVycyI6WyJnb29nbGUiXX0sInVzZXJfbWV0YWRhdGEiOnsiYXZhdGFyX3VybCI6Imh0dHBzOi8vbGgzLmdvb2dsZXVzZXJjb250ZW50LmNvbS9hL0FHTm15eFp0OHNUUk1LWEFlOXpTb2VOcHFZdU8xbnBvMi1URnFHdWVFejlzPXM5Ni1jIiwiZW1haWwiOiJ2b3ZjaHlraGFsYW1haGFAZ21haWwuY29tIiwiZW1haWxfdmVyaWZpZWQiOnRydWUsImZ1bGxfbmFtZSI6IlZvbG9keW15ciIsImlzcyI6Imh0dHBzOi8vd3d3Lmdvb2dsZWFwaXMuY29tL3VzZXJpbmZvL3YyL21lIiwibmFtZSI6IlZvbG9keW15ciIsInBpY3R1cmUiOiJodHRwczovL2xoMy5nb29nbGV1c2VyY29udGVudC5jb20vYS9BR05teXhadDhzVFJNS1hBZTl6U29lTnBxWXVPMW5wbzItVEZxR3VlRXo5cz1zOTYtYyIsInByb3ZpZGVyX2lkIjoiMTAxOTA5MzYyNjAxNzM3NTIwNDA2Iiwic3ViIjoiMTAxOTA5MzYyNjAxNzM3NTIwNDA2In0sInJvbGUiOiJhdXRoZW50aWNhdGVkIiwiYWFsIjoiYWFsMSIsImFtciI6W3sibWV0aG9kIjoib2F1dGgiLCJ0aW1lc3RhbXAiOjE2NzgzOTkyMTF9XSwic2Vzc2lvbl9pZCI6Ijk1OWFiYzljLTYzZTctNGNiOC1iZDM5LWMzNGU5ODNlNmNlNSJ9.uykgrl1l9Lk-9jzdDTKuVUflGqhabHvWDvPCedRvsbA; AMP_8f1ede8e9c=JTdCJTIyZGV2aWNlSWQlMjIlM0ElMjIwNGU4ZDZmMS1kN2JkLTRiYTMtODE0Zi1mZDk3MTBkNDJjNDYlMjIlMkMlMjJzZXNzaW9uSWQlMjIlM0ExNjc4Mzk3ODE0OTU4JTJDJTIyb3B0T3V0JTIyJTNBZmFsc2UlMkMlMjJsYXN0RXZlbnRUaW1lJTIyJTNBMTY3ODM5OTM3NzIyNyU3RA==");

            var document1 = await _awsStorage.DownloadFileAsync(document.document);

            var json = JsonSerializer.Serialize(new Documents { document = document1 });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var result = await httpClient.PostAsync("https://api.gptzero.me/v2/predict/text", content);

            var response = await result.Content.ReadAsStringAsync();
            return Ok(response);
        }

        [HttpPost]
        [Route("test")]
        public async Task<object> Test(Documents document)
        {
            var document1 = await _awsStorage.DownloadFileAsync(document.document);

            AntlrInputStream inputStream1 = new AntlrInputStream(document1);
            JavaLexer speakLexer1 = new JavaLexer(inputStream1);
            CommonTokenStream commonTokenStream1 = new CommonTokenStream(speakLexer1);
            JavaParser parser1 = new JavaParser(commonTokenStream1);

            // Parse the code and get the parse tree
            JavaParser.CompilationUnitContext parseTree1 = parser1.compilationUnit();

            var document2 = await _awsStorage.DownloadFileAsync("test1.java");

            AntlrInputStream inputStream2 = new AntlrInputStream(document2);
            JavaLexer speakLexer2 = new JavaLexer(inputStream2);
            CommonTokenStream commonTokenStream2 = new CommonTokenStream(speakLexer2);
            JavaParser parser2 = new JavaParser(commonTokenStream2);

            // Parse the code and get the parse tree
            JavaParser.CompilationUnitContext parseTree2 = parser2.compilationUnit();
            // Create a listener or visitor and walk the parse tree to build the AST
            var listener = new MyListener();
            ParseTreeWalker.Default.Walk(listener, parseTree1);

            // Get the methods from the listener
            List<MethodNode> methods = listener.GetMethods(); 

            var res =TreeComparer.TreeEditDistance(parseTree1, parseTree2);

            var astVisitor = new JavaParserBaseVisitor<ASTNode>();
            var ast = parseTree1.Accept(astVisitor);
            var jsonTest = JsonSerializer.Serialize(ast);
            return Ok(ast);
        }

        [HttpPost]
        [Route("tokenize")]
        public async Task<object> Tokenize(FilesToCompare files)
        {
            var documents = files.FileNames.Select(x => _awsStorage.DownloadFileAsync(x));
            var asts = new List<Node>();
            var code = new List<string>();
            foreach (var document in documents)
            {
                var doc = await document;
                var inputStream = new AntlrInputStream(doc);
                var speakLexer = new JavaLexer(inputStream);
                var commonTokenStream = new CommonTokenStream(speakLexer);
                var parser = new JavaParser(commonTokenStream);
                var parseTree = parser.compilationUnit();
                var astVisitor = new NewJavaAstVisitor();
                var ast = parseTree.Accept(astVisitor);
                asts.Add(ast);
                code.Add(doc);
            }
            
            var comparer = new AstComparer();
            var similarity = AstSimilarity.CalculateSimilarities(code);

            var settings = new JsonSerializerSettings();
            settings.NullValueHandling = NullValueHandling.Ignore;

            return Ok(new { Ast1 = asts[0], Ast2 = asts[1], Similarity = similarity });
        }

        [HttpPost]
        [Route("compare")]
        public async Task<object> Compare(MyTrees trees)
        {
            var comparer = new AstComparer();
            var result = comparer.GetSimilarityPercentage(trees.Node1, trees.Node2);
            return Ok(result);
        }
        
        [HttpGet]
        [Route("get-results")]
        public async Task<object> GetResults()
        {
            using var clientHandler = new HttpClientHandler();
            using var httpClient = new HttpClient(clientHandler);
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer BFFB0EE3995C3BE5F0E14A21FD6CC1FA63F9445EF01077351497BE119FE9B9DA");

            var result = await httpClient.GetAsync("https://app.copyleaks.com/api/v1/dashboard/scan/4dv7prm6qmizzrf9/report/complete-results");

            var response = await result.Content.ReadAsStringAsync();
            return Ok(response);
        }
    }
}

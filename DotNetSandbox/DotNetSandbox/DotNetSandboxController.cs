namespace DotNetSandbox
{
    using Microsoft.CSharp;
    using Newtonsoft.Json;
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Http;
    using System.Web.Http.Cors;

    [EnableCors("*", "*", "*")]
    public class DotNetSandboxController : ApiController
    {
        struct Error
        {
            [JsonProperty("error_code")]
            public string ErrorCode { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }

        private static IEnumerable<string> GetReferences(string code)
        {
            var lines = code.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(_ => _.TrimStart());
            foreach (var line in lines)
            {
                // Read until we meet first non-commnet line
                if (!line.StartsWith("//")) break;
                // If it's a normal comment, skip
                if (!line.StartsWith("//!!")) continue;
                yield return line.Substring(4).Trim();
            }
        }

        private static Assembly Compile(string source)
        {
            Dictionary<string, string> providerOptions = new Dictionary<string, string>
            {
                { "CompilerVersion", "v4.0" }
            };

            Environment.CurrentDirectory = HttpContext.Current.Server.MapPath("~/ReferencedAssemblies");
            CSharpCodeProvider provider = new CSharpCodeProvider(providerOptions);
            CompilerParameters compilerParams = new CompilerParameters(GetReferences(source).ToArray())
            {
                GenerateInMemory = true,
                GenerateExecutable = true,
            };

            CompilerResults results = provider.CompileAssemblyFromSource(compilerParams, source);
            if (results.Errors.Count != 0)
            {
                StringBuilder builder = new StringBuilder();
                foreach (var e in results.Errors)
                {
                    builder.AppendLine(e.ToString());
                }

                throw new ArgumentException(builder.ToString());
            }

            return results.CompiledAssembly;
        }

        private static string Run(Assembly assembly)
        {
            using (var writer = new StringWriter())
            {
                var entrypoint = assembly.EntryPoint;
                Console.SetOut(writer);
                Console.SetError(writer);
                try
                {
                    entrypoint.Invoke(null, null);
                    return writer.ToString();
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                    {
                        return ex.InnerException.ToString();
                    }

                    throw;
                }
            }
        }

        private async Task<string> GetSource()
        {
            if (Request.Content.Headers.ContentType != null && Request.Content.Headers.ContentType.MediaType != "text/plain")
            {
                throw new InvalidDataException(string.Format("Unsupported content-type: {0}, only text/plain is supported.", Request.Content.Headers.ContentType.MediaType));
            }

            return await Request.Content.ReadAsStringAsync();
        }

        [Route("compile")]
        [HttpPost]
        public async Task<HttpResponseMessage> Compile()
        {
            string source;
            Assembly assembly;
            try
            {
                source = await GetSource();
            }
            catch (InvalidDataException ex)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new Error() { ErrorCode = "invalid_content_type", ErrorMessage = ex.Message });
            }
            try
            {
                assembly = Compile(source);
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (ArgumentException ex)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new Error() { ErrorCode = "compile_error", ErrorMessage = ex.Message });
            }
        }

        [Route("run")]
        [HttpPost]
        public async Task<HttpResponseMessage> Run()
        {
            string source;
            Assembly assembly;
            try
            {
                source = await GetSource();
            }
            catch (InvalidDataException ex)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new Error() { ErrorCode = "invalid_content_type", ErrorMessage = ex.Message });
            }
            try
            {
                assembly = Compile(source);
            }
            catch (ArgumentException ex)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new Error() { ErrorCode = "compile_error", ErrorMessage = ex.Message });
            }

            try
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = new StringContent(Run(assembly), Encoding.UTF8, "text/plain");
                return response;
            }
            catch (TargetParameterCountException)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new Error() { ErrorCode = "invalid_main_args", ErrorMessage = "The parameters of Main method must be void." });
            }
        }
    }
}

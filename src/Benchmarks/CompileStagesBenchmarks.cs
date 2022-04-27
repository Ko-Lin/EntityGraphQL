using BenchmarkDotNet.Attributes;
using EntityGraphQL;
using EntityGraphQL.Schema;

namespace Benchmarks
{
    /// <summary>
    /// Comparing the different stages of compiling.
    /// Stage 1 - query to Expressions and metadata. This can be cached and reused with different variables
    /// Stage 2 - the stage1 result into a final LambdaExpression that is executed. This may be built twice and executed twice, 
    /// once without service field and then with
    /// 
    /// 1.2.x
    /// |             Method |     Mean |   Error |  StdDev |   Gen 0 | Allocated |
    /// |------------------- |---------:|--------:|--------:|--------:|----------:|
    /// |  FirstStageCompile | 164.9 us | 3.28 us | 4.60 us | 18.0664 |     38 KB |
    /// | SecondStageCompile | 103.7 us | 1.57 us | 1.40 us | 25.1465 |     52 KB |  
    ///                Total | 268.6                                        90
    /// 
    /// 2.0.0
    /// |             Method |     Mean |   Error |  StdDev |   Gen 0 | Allocated |
    /// |------------------- |---------:|--------:|--------:|--------:|----------:|
    /// |            Compile | 130.8 us | 0.52 us | 0.49 us | 29.2969 |     60 KB |
    /// 
    /// </summary>
    [MemoryDiagnoser]
    public class CompileStagesBenchmarks : BaseBenchmark
    {
        private readonly string query = @"{
                    movie(id: ""077b3041-307a-42ba-9ffe-1121fcfc918b"") {
                        id name released
                        director {
                            id name dob
                        }
                        actors {
                            id name dob
                        }
                    }
                }";

        private readonly QueryRequest gql;
        private readonly BenchmarkContext context;

        public CompileStagesBenchmarks()
        {
            gql = new QueryRequest
            {
                Query = query
            };
            context = GetContext();
        }

        [Benchmark]
        public void Compile()
        {
            Schema.ExecuteRequest(gql, context, null, null, new ExecutionOptions { NoExecution = true });
        }
    }
}
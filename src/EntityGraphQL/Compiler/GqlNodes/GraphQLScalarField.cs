using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Schema.FieldExtensions;

namespace EntityGraphQL.Compiler
{
    public class GraphQLScalarField : BaseGraphQLField
    {
        private readonly ExpressionExtractor extractor;
        private readonly ParameterReplacer replacer;
        private List<GraphQLScalarField> extractedFields;

        public GraphQLScalarField(IEnumerable<IFieldExtension> fieldExtensions, string name, Expression nextContextExpression, ParameterExpression rootParameter, IGraphQLNode parentNode)
            : base(name, nextContextExpression, rootParameter, parentNode)
        {
            this.fieldExtensions = fieldExtensions?.ToList();
            Name = name;
            extractor = new ExpressionExtractor();
            replacer = new ParameterReplacer();
        }

        public override bool HasAnyServices(IEnumerable<GraphQLFragmentStatement> fragments)
        {
            return Services.Any();
        }

        public override IEnumerable<BaseGraphQLField> Expand(List<GraphQLFragmentStatement> fragments, bool withoutServiceFields)
        {
            if (withoutServiceFields && Services.Any())
            {
                var extractedFields = ExtractFields();
                if (extractedFields != null)
                    return extractedFields;
            }
            return new List<BaseGraphQLField> { this };
        }

        private IEnumerable<BaseGraphQLField> ExtractFields()
        {
            if (extractedFields != null)
                return extractedFields;

            extractedFields = extractor.Extract(NextContextExpression, RootParameter)?.Select(i => new GraphQLScalarField(null, i.Key, i.Value, RootParameter, ParentNode)).ToList();
            return extractedFields;
        }

        public override Expression GetNodeExpression(IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, ParameterExpression schemaContext, bool withoutServiceFields, Expression replaceContextWith = null, bool isRoot = false, bool useReplaceContextDirectly = false)
        {
            if (withoutServiceFields && Services.Any())
                return null;

            var newExpression = NextContextExpression;
            if (replaceContextWith != null)
            {
                var selectedField = replaceContextWith.Type.GetField(Name);
                if (!Services.Any() && selectedField != null)
                    newExpression = Expression.Field(replaceContextWith, Name);
                else
                    newExpression = replacer.ReplaceByType(NextContextExpression, ParentNode.NextContextExpression.Type, replaceContextWith);

            }
            newExpression = ProcessScalarExpression(newExpression, replacer);
            return newExpression;
        }
    }
}
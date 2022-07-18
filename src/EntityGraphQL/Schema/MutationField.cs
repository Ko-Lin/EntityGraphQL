using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EntityGraphQL.Compiler;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Schema
{
    public class MutationField : BaseField
    {
        public override GraphQLQueryFieldType FieldType { get; } = GraphQLQueryFieldType.Mutation;
        private readonly object? mutationClassInstance;
        private readonly MethodInfo method;
        private readonly bool isAsync;

        public MutationField(ISchemaProvider schema, string methodName, GqlTypeInfo returnType, object? mutationClassInstance, MethodInfo method, string description, RequiredAuthorization requiredAuth, bool isAsync, Func<string, string> fieldNamer, bool autoAddInputTypes)
            : base(schema, methodName, description, returnType)
        {
            Services = new List<Type>();
            this.mutationClassInstance = mutationClassInstance;
            this.method = method;
            RequiredAuthorization = requiredAuth;
            this.isAsync = isAsync;

            ArgumentsType = method.GetParameters()
                .SingleOrDefault(p => p.GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null || p.ParameterType.GetTypeInfo().GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null)?.ParameterType;
            if (ArgumentsType != null)
            {
                foreach (var item in ArgumentsType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (GraphQLIgnoreAttribute.ShouldIgnoreMemberFromInput(item))
                        continue;
                    Arguments.Add(fieldNamer(item.Name), ArgType.FromProperty(schema, item, null, fieldNamer));
                    AddInputTypesInArguments(schema, autoAddInputTypes, item.PropertyType);

                }
                foreach (var item in ArgumentsType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (GraphQLIgnoreAttribute.ShouldIgnoreMemberFromInput(item))
                        continue;
                    Arguments.Add(fieldNamer(item.Name), ArgType.FromField(schema, item, null, fieldNamer));
                    AddInputTypesInArguments(schema, autoAddInputTypes, item.FieldType);
                }
            }
        }

        private static void AddInputTypesInArguments(ISchemaProvider schema, bool autoAddInputTypes, Type propType)
        {
            var inputType = propType.GetEnumerableOrArrayType() ?? propType;
            if (autoAddInputTypes && !schema.HasType(inputType))
                schema.AddInputType(inputType, inputType.Name, null).AddAllFields(true);
        }

        public void Deprecate(string reason)
        {
            IsDeprecated = true;
            DeprecationReason = reason;
        }

        public async Task<object?> CallAsync(object? context, IReadOnlyDictionary<string, object>? gqlRequestArgs, GraphQLValidator validator, IServiceProvider? serviceProvider, ParameterExpression? variableParameter, object? docVariables)
        {
            if (context == null)
                return null;

            // args in the mutation method
            var allArgs = new List<object>();
            object? argInstance = null;

            if (Arguments.Count > 0)
            {
                argInstance = ArgumentUtil.BuildArgumentsObject(Schema, Name, this, gqlRequestArgs ?? new Dictionary<string, object>(), Arguments.Values, ArgumentsType, variableParameter, docVariables);
            }

            // add parameters and any DI services
            foreach (var p in method.GetParameters())
            {
                if (p.GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null || p.ParameterType.GetTypeInfo().GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null)
                {
                    allArgs.Add(argInstance!);
                }
                else if (p.ParameterType == context.GetType())
                {
                    allArgs.Add(context);
                }
                // todo we should put this in the IServiceCollection actually...
                else if (p.ParameterType == typeof(GraphQLValidator))
                {
                    allArgs.Add(validator);
                }
                else if (serviceProvider != null)
                {
                    var service = serviceProvider.GetService(p.ParameterType);
                    if (service == null)
                    {
                        throw new EntityGraphQLExecutionException($"Service {p.ParameterType.Name} not found for dependency injection for mutation {method.Name}");
                    }
                    allArgs.Add(service);
                }
            }

            if (argumentValidators.Count > 0)
            {
                var validatorContext = new ArgumentValidatorContext(this, argInstance);
                foreach (var argValidator in argumentValidators)
                {
                    argValidator(validatorContext);
                    argInstance = validatorContext.Arguments;
                }
                if (validatorContext.Errors.Count > 0)
                {
                    throw new EntityGraphQLValidationException(validatorContext.Errors);
                }
            }

            object? instance = mutationClassInstance;
            if (instance == null)
            {
                //try instantiate the mutation class using the service provider
                if (serviceProvider != null)
                {
                    instance = serviceProvider.GetService(method.DeclaringType);
                }

                //fallback to activator create instance
                if (instance == null)
                {
                    instance = Activator.CreateInstance(method.DeclaringType);
                }
            }

            object? result;
            if (isAsync)
            {
                result = await (dynamic?)method.Invoke(instance, allArgs.Any() ? allArgs.ToArray() : null);
            }
            else
            {
                try
                {
                    result = method.Invoke(instance, allArgs.ToArray());
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                        throw ex.InnerException;
                    throw;
                }
            }
            return result;
        }

        public override (Expression? expression, object? argumentValues) GetExpression(Expression fieldExpression, Expression? fieldContext, IGraphQLNode? parentNode, ParameterExpression? schemaContext, Dictionary<string, object> args, ParameterExpression? docParam, object? docVariables, IEnumerable<GraphQLDirective> directives, bool contextChanged, ParameterReplacer replacer)
        {
            var result = fieldExpression;

            if (schemaContext != null)
            {
                result = replacer.ReplaceByType(result, schemaContext.Type, schemaContext);
            }
            return (result, null);
        }
    }
}
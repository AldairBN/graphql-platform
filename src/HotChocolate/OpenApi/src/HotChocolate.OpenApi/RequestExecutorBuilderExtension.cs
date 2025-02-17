using System.Text.Json;
using HotChocolate.Execution.Configuration;
using HotChocolate.Resolvers;
using HotChocolate.Skimmed;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Readers;
using static HotChocolate.OpenApi.Properties.OpenApiResources;
using IField = HotChocolate.Skimmed.IField;
using InputObjectType = HotChocolate.Skimmed.InputObjectType;
using ObjectType = HotChocolate.Skimmed.ObjectType;

namespace HotChocolate.OpenApi;

public static class RequestExecutorBuilderExtension
{
    public static IRequestExecutorBuilder AddOpenApi(
        this IRequestExecutorBuilder builder,
        string clientName,
        string openApi)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        ArgumentException.ThrowIfNullOrEmpty(openApi);

        var documentReader = new OpenApiStringReader();
        var wrapper = new OpenApiWrapper();

        var document = documentReader.Read(openApi, out _);
        var schema = wrapper.Wrap(clientName, document);

        builder.AddJsonSupport();
        builder.InitializeSchema(schema);

        return builder;
    }

    private static void InitializeSchema(
        this IRequestExecutorBuilder requestExecutorBuilder,
        Skimmed.Schema schema)
    {
        if (schema.QueryType is { } queryType)
        {
            requestExecutorBuilder.AddQueryType(SetupType(queryType));
        }

        if (schema.MutationType is { } mutationType)
        {
            requestExecutorBuilder.AddMutationType(SetupType(mutationType));
        }

        foreach (var type in schema.Types.OfType<ObjectType>())
        {
            requestExecutorBuilder.AddObjectType(SetupType(type));
        }

        foreach (var type in schema.Types.OfType<InputObjectType>())
        {
            requestExecutorBuilder.AddInputObjectType(SetupInputType(type));
        }
    }

    private static Action<IObjectTypeDescriptor> SetupType(ComplexType skimmedType) =>
        desc =>
        {
            desc.Name(skimmedType.Name)
                .Description(skimmedType.Description);

            foreach (var field in skimmedType.Fields)
            {
                var fieldDescriptor = CreateFieldDescriptor(field, desc);

                foreach (var fieldArgument in field.Arguments)
                {
                    fieldDescriptor.Argument(
                        fieldArgument.Name,
                        descriptor => descriptor
                            .Type(fieldArgument.Type.ToTypeNode())
                            .Description(fieldArgument.Description));
                }

                if (field.ContextData.TryGetValue(ContextResolverParameter, out var res) &&
                    res is Func<IResolverContext, Task<JsonElement>> resolver)
                {
                    fieldDescriptor.Resolve(ctx => resolver.Invoke(ctx));
                }
                else
                {
                    var propertyName = field.ContextData.TryGetValue(OpenApiPropertyName, out var name)
                        ? name?.ToString()
                        : null;
                    fieldDescriptor.FromJson(propertyName);
                }
            }
        };

    private static IObjectFieldDescriptor CreateFieldDescriptor(IField field, IObjectTypeDescriptor desc)
    {
        var fieldDescriptor = desc.Field(field.Name)
            .Description(field.Description)
            .Type(field.Type.ToTypeNode());

        return fieldDescriptor;
    }

    private static Action<IInputObjectTypeDescriptor> SetupInputType(InputObjectType skimmedType) =>
        desc =>
        {
            desc.Name(skimmedType.Name)
                .Description(skimmedType.Description);

            foreach (var field in skimmedType.Fields)
            {
                desc.Field(field.Name)
                    .Description(field.Description)
                    .Type(field.Type.ToTypeNode());
            }
        };
}

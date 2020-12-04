using System.Collections.Generic;
using System.Linq;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using StrawberryShake.CodeGeneration.Analyzers;
using StrawberryShake.CodeGeneration.Analyzers.Models;
using StrawberryShake.CodeGeneration.Utilities;

namespace StrawberryShake.CodeGeneration.Analyzers
{
    internal class UnionTypeSelectionSetAnalyzer
        : SelectionSetAnalyzerBase<UnionType>
    {
        public override void Analyze(
            IDocumentAnalyzerContext context,
            OperationDefinitionNode operation,
            FieldNode fieldSelection,
            PossibleSelections possibleSelections,
            IType fieldType,
            UnionType namedType,
            Path path)
        {
            IFragmentNode returnTypeFragment =
                ResolveReturnType(
                    namedType,
                    fieldSelection,
                    possibleSelections.ReturnType);

            ComplexOutputTypeModel returnType =
                CreateInterfaceModel(
                    context,
                    returnTypeFragment,
                    path);

            CreateClassModels(
                context,
                operation,
                fieldSelection,
                possibleSelections,
                returnType,
                fieldType,
                path);
        }

        private void CreateClassModels(
            IDocumentAnalyzerContext context,
            OperationDefinitionNode operation,
            FieldNode fieldSelection,
            PossibleSelections possibleSelections,
            ComplexOutputTypeModel returnType,
            IType fieldType,
            Path path)
        {
            IReadOnlyCollection<SelectionInfo> selections = possibleSelections.Variants;

            IReadOnlyList<ComplexOutputTypeModel> modelTypes =
                CreateClassModels(
                    context,
                    returnType,
                    fieldSelection,
                    selections,
                    path);

            CreateFieldParserModel(
                context,
                operation,
                fieldSelection,
                path,
                returnType,
                fieldType,
                modelTypes);
        }
    }
}

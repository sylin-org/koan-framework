using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions.Instructions;

/// <summary>Exact effect classification for framework-owned instruction identities.</summary>
public static class InstructionEffect
{
    public static DataOperationEffect EffectiveEffect(this Instruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Effect != DataOperationEffect.Unknown) return instruction.Effect;

        return instruction.Name switch
        {
            DataInstructions.EnsureCreated => DataOperationEffect.SchemaOrAdmin,
            DataInstructions.Clear => DataOperationEffect.Write,
            DataInstructions.Patch => DataOperationEffect.Write,
            RelationalInstructions.SchemaValidate => DataOperationEffect.Read,
            RelationalInstructions.SchemaEnsureCreated => DataOperationEffect.SchemaOrAdmin,
            RelationalInstructions.SchemaClear => DataOperationEffect.SchemaOrAdmin,
            _ => DataOperationEffect.Unknown
        };
    }
}

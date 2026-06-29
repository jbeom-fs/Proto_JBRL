using System;
using System.Collections.Generic;
using System.Text;

public sealed class DeveloperConsoleService
{
    private delegate DeveloperConsoleCommandResult CommandHandler(string arguments);
    private delegate void ArgumentSuggestionProvider(string currentArg, List<string> output, int maxCount);
    private delegate void SubArgumentSuggestionProvider(string subCommand, string currentArg, List<string> output, int maxCount);

    private const string GiveUsage = "Usage: /give <category> <code> [count]";
    private const string GivePositiveCountUsage = "Usage: /give <category> <code> [positiveCount]";
    private const string EnhanceUsage = "Usage: /enhance <form> <stat> [count]";
    private const string EnhancePositiveCountUsage = "Usage: /enhance <form> <stat> [positiveCount]";
    private const string EngravingUsage = "Usage: /engraving <give <form> <index> | equip <slot> <poolIndex> | unequip <slot> | show>";

    private static readonly string[] s_FloorArgs = { "add", "sub", "set" };
    private static readonly string[] s_DoorOpenArgs = { "normal", "elite" };
    private static readonly string[] s_FormArgs = { "set" };
    private static readonly string[] s_EngravingArgs = { "give", "equip", "unequip", "show" };

    private readonly Dictionary<string, CommandHandler> _commands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ArgumentSuggestionProvider> _argumentProviders = new Dictionary<string, ArgumentSuggestionProvider>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SubArgumentSuggestionProvider> _subArgumentProviders =
        new Dictionary<string, SubArgumentSuggestionProvider>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _destinationIdBuffer = new List<string>(16);
    private readonly DeveloperConsoleCommandExecutor _executor;

    public DeveloperConsoleService(DeveloperConsoleCommandExecutor executor)
    {
        _executor = executor;
        RegisterDefaults();
    }

    public void GetCommandNames(List<string> output)
    {
        if (output == null)
            return;

        foreach (string name in _commands.Keys)
            output.Add(name);
    }

    public void GetArgumentSuggestions(string commandName, string currentArg, List<string> output, int maxCount)
    {
        if (output == null)
            return;

        if (string.IsNullOrWhiteSpace(commandName))
            return;

        if (!_argumentProviders.TryGetValue(commandName, out ArgumentSuggestionProvider provider))
            return;

        provider(currentArg, output, maxCount);
    }

    public void GetSubArgumentSuggestions(string commandName, string subCommand, string currentArg, List<string> output, int maxCount)
    {
        if (output == null || string.IsNullOrWhiteSpace(commandName) || string.IsNullOrWhiteSpace(subCommand))
            return;

        if (!_subArgumentProviders.TryGetValue(commandName, out SubArgumentSuggestionProvider provider))
            return;

        provider(subCommand, currentArg, output, maxCount);
    }

    public DeveloperConsoleCommandResult Execute(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return DeveloperConsoleCommandResult.Ignored();

        string trimmed = input.Trim();
        int separatorIndex = trimmed.IndexOf(' ');
        string commandName = separatorIndex >= 0 ? trimmed.Substring(0, separatorIndex) : trimmed;
        string arguments = separatorIndex >= 0 ? trimmed.Substring(separatorIndex + 1).Trim() : string.Empty;
        if (commandName.Length > 0 && commandName[0] == '/')
            commandName = commandName.Substring(1);

        if (!_commands.TryGetValue(commandName, out CommandHandler handler))
            return DeveloperConsoleCommandResult.Error("Unknown command: " + commandName);

        return handler(arguments);
    }

    private void RegisterDefaults()
    {
        _commands["help"] = ExecuteHelp;
        _commands["clear"] = ExecuteClear;
        _commands["echo"] = ExecuteEcho;
        _commands["tp"] = ExecuteTeleport;
        _commands["dooropen"] = ExecuteDoorOpen;
        _commands["kill"] = ExecuteKill;
        _commands["floor"] = ExecuteFloor;
        _commands["form"] = ExecuteForm;
        _commands["give"] = ExecuteGive;
        _commands["enhance"] = ExecuteEnhance;
        _commands["engraving"] = ExecuteEngraving;

        _argumentProviders["floor"] = ProvideFloorSuggestions;
        _argumentProviders["form"] = ProvideFormSuggestions;
        _argumentProviders["give"] = ProvideGiveCategorySuggestions;
        _argumentProviders["enhance"] = ProvideEnhanceFormSuggestions;
        _argumentProviders["engraving"] = ProvideEngravingSuggestions;
        _argumentProviders["dooropen"] = ProvideDoorOpenSuggestions;
        _argumentProviders["tp"] = ProvideTeleportSuggestions;

        _subArgumentProviders["form"] = ProvideFormSubArgumentSuggestions;
        _subArgumentProviders["give"] = ProvideGiveItemCodeSuggestions;
        _subArgumentProviders["enhance"] = ProvideEnhanceStatSuggestions;
    }

    private static void ProvideFloorSuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(s_FloorArgs, currentArg, output, maxCount);

    private static void ProvideDoorOpenSuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(s_DoorOpenArgs, currentArg, output, maxCount);

    private void ProvideTeleportSuggestions(string currentArg, List<string> output, int maxCount)
    {
        if (_executor == null)
            return;

        _destinationIdBuffer.Clear();
        _executor.GetTeleportDestinationIds(_destinationIdBuffer);
        FilterSuggestions(_destinationIdBuffer, currentArg, output, maxCount);
    }

    private static void ProvideFormSuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(s_FormArgs, currentArg, output, maxCount);

    private static void ProvideEngravingSuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(s_EngravingArgs, currentArg, output, maxCount);

    private static void ProvideGiveCategorySuggestions(string currentArg, List<string> output, int maxCount)
        => FilterSuggestions(DeveloperConsoleItemCategoryResolver.CategoryTokens, currentArg, output, maxCount);

    private void ProvideEnhanceFormSuggestions(string currentArg, List<string> output, int maxCount)
    {
        _destinationIdBuffer.Clear();
        _executor?.GetFormIds(_destinationIdBuffer);
        FilterSuggestions(_destinationIdBuffer, currentArg, output, maxCount);
    }

    private void ProvideFormSubArgumentSuggestions(string subCommand, string currentArg, List<string> output, int maxCount)
    {
        if (!string.Equals(subCommand, "set", StringComparison.OrdinalIgnoreCase))
            return;

        _destinationIdBuffer.Clear();
        _executor?.GetFormIds(_destinationIdBuffer);
        FilterSuggestions(_destinationIdBuffer, currentArg, output, maxCount);
    }

    private void ProvideGiveItemCodeSuggestions(string subCommand, string currentArg, List<string> output, int maxCount)
    {
        if (!DeveloperConsoleItemCategoryResolver.TryResolveCategory(subCommand, out ItemType itemType))
            return;

        _destinationIdBuffer.Clear();
        _executor?.GetItemCodes(itemType, _destinationIdBuffer);
        FilterSuggestions(_destinationIdBuffer, currentArg, output, maxCount);
    }

    private static void ProvideEnhanceStatSuggestions(string subCommand, string currentArg, List<string> output, int maxCount)
    {
        if (!Enum.TryParse(subCommand, true, out PlayerFormId _))
            return;

        FilterSuggestions(DeveloperConsoleSoulStatResolver.StatTokens, currentArg, output, maxCount);
    }

    private static void FilterSuggestions(IReadOnlyList<string> candidates, string prefix, List<string> output, int maxCount)
    {
        for (int i = 0; i < candidates.Count && output.Count < maxCount; i++)
        {
            if (string.IsNullOrEmpty(prefix) ||
                candidates[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                output.Add(candidates[i]);
        }
    }

    private DeveloperConsoleCommandResult ExecuteHelp(string arguments)
    {
        StringBuilder builder = new StringBuilder();
        bool first = true;
        foreach (string commandName in _commands.Keys)
        {
            if (!first)
                builder.Append(", ");

            builder.Append(commandName);
            first = false;
        }

        return DeveloperConsoleCommandResult.Success(
            "Commands: " + builder +
            "\nUsage: /TP [destinationId]" +
            "\nUsage: /DoorOpen [normal|elite]" +
            "\nUsage: /kill" +
            "\nUsage: /form set [id]" +
            "\n" + GiveUsage +
            "\n" + EnhanceUsage +
            "\n" + EngravingUsage +
            "\nUsage: /floor add [count] | /floor sub [count] | /floor set [floor]");
    }

    private DeveloperConsoleCommandResult ExecuteClear(string arguments)
        => DeveloperConsoleCommandResult.Clear();

    private DeveloperConsoleCommandResult ExecuteEcho(string arguments)
        => DeveloperConsoleCommandResult.Success(arguments);

    private DeveloperConsoleCommandResult ExecuteTeleport(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return DeveloperConsoleCommandResult.Error("Usage: /TP [destinationId]");

        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        string destinationId = arguments.Trim();
        if (destinationId.IndexOf(' ') >= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /TP [destinationId]");

        return _executor.ExecuteTeleport(destinationId);
    }

    private DeveloperConsoleCommandResult ExecuteDoorOpen(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        string doorType = string.IsNullOrWhiteSpace(arguments) ? "normal" : arguments.Trim();
        if (doorType.IndexOf(' ') >= 0)
            return DeveloperConsoleCommandResult.Error("Usage: /DoorOpen [normal|elite]");

        if (string.Equals(doorType, "normal", StringComparison.OrdinalIgnoreCase))
            return _executor.ExecuteOpenNormalDoors();

        if (string.Equals(doorType, "elite", StringComparison.OrdinalIgnoreCase))
            return _executor.ExecuteOpenEliteDoors();

        return DeveloperConsoleCommandResult.Error("Unsupported DoorType: " + doorType + ". Use normal or elite.");
    }

    private DeveloperConsoleCommandResult ExecuteKill(string arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments))
            return DeveloperConsoleCommandResult.Error("Usage: /kill");

        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        return _executor.ExecuteKill();
    }

    private DeveloperConsoleCommandResult ExecuteFloor(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        if (!TryReadFloorArguments(arguments, out string subCommand, out string valueText))
            return DeveloperConsoleCommandResult.Error(GetFloorUsage());

        if (string.Equals(subCommand, "add", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePositiveOptionalCount(valueText, out int count))
                return DeveloperConsoleCommandResult.Error("Usage: /floor add [positiveCount]");
            return _executor.ExecuteFloorAdd(count);
        }

        if (string.Equals(subCommand, "sub", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePositiveOptionalCount(valueText, out int count))
                return DeveloperConsoleCommandResult.Error("Usage: /floor sub [positiveCount]");
            return _executor.ExecuteFloorSub(count);
        }

        if (string.Equals(subCommand, "set", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(valueText))
                return DeveloperConsoleCommandResult.Error("Usage: /floor set [floor]");
            if (!TryParsePositiveInt(valueText, out int targetFloor))
                return DeveloperConsoleCommandResult.Error("Usage: /floor set [floor]");
            return _executor.ExecuteFloorSet(targetFloor);
        }

        return DeveloperConsoleCommandResult.Error(GetFloorUsage());
    }

    private DeveloperConsoleCommandResult ExecuteForm(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        if (!TryReadFloorArguments(arguments, out string subCommand, out string valueText))
            return DeveloperConsoleCommandResult.Error("Usage: /form set [id]");

        if (!string.Equals(subCommand, "set", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(valueText))
        {
            return DeveloperConsoleCommandResult.Error("Usage: /form set [id]");
        }

        return _executor.ExecuteSetForm(valueText);
    }

    private DeveloperConsoleCommandResult ExecuteGive(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        if (!TryReadGiveArguments(arguments, out string categoryToken, out string itemCode, out string countText))
            return DeveloperConsoleCommandResult.Error(GiveUsage);

        if (!DeveloperConsoleItemCategoryResolver.TryResolveCategory(categoryToken, out ItemType requestedType))
            return DeveloperConsoleCommandResult.Error("Unknown item category: " + categoryToken + ". " + GiveUsage);

        if (!TryParsePositiveOptionalCount(countText, out int count))
            return DeveloperConsoleCommandResult.Error(GivePositiveCountUsage);

        if (!_executor.TryGetItemType(itemCode, out ItemType actualType))
            return DeveloperConsoleCommandResult.Error("Unknown itemCode: " + itemCode + ". " + GiveUsage);

        if (actualType != requestedType)
        {
            return DeveloperConsoleCommandResult.Error(
                "Item category mismatch: " + itemCode +
                " is " + DeveloperConsoleItemCategoryResolver.GetCategoryToken(actualType) +
                ", not " + DeveloperConsoleItemCategoryResolver.GetCategoryToken(requestedType) + ". " +
                GiveUsage);
        }

        return _executor.ExecuteItemGive(itemCode, count);
    }

    private DeveloperConsoleCommandResult ExecuteEnhance(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        if (!TryReadEnhanceArguments(arguments, out string formToken, out string statToken, out string countText))
            return DeveloperConsoleCommandResult.Error(EnhanceUsage);

        if (!Enum.TryParse(formToken, true, out PlayerFormId form))
            return DeveloperConsoleCommandResult.Error("Unknown form: " + formToken + ". " + EnhanceUsage);

        if (!DeveloperConsoleSoulStatResolver.TryResolve(statToken, out SoulStatType stat))
            return DeveloperConsoleCommandResult.Error("Unknown soul stat: " + statToken + ". " + GetEnhanceStatUsage());

        if (!TryParsePositiveOptionalCount(countText, out int count))
            return DeveloperConsoleCommandResult.Error(EnhancePositiveCountUsage);

        return _executor.ExecuteEnhance(form, stat, count);
    }

    private DeveloperConsoleCommandResult ExecuteEngraving(string arguments)
    {
        if (_executor == null)
            return DeveloperConsoleCommandResult.Error("Command executor is not assigned.");

        if (string.IsNullOrWhiteSpace(arguments))
            return DeveloperConsoleCommandResult.Error(EngravingUsage);

        string[] parts = arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && string.Equals(parts[0], "show", StringComparison.OrdinalIgnoreCase))
            return _executor.ExecuteEngravingShow();

        if (parts.Length == 3 && string.Equals(parts[0], "give", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseZeroBasedInt(parts[2], out int debugIndex))
                return DeveloperConsoleCommandResult.Error(EngravingUsage);

            return _executor.ExecuteEngravingGive(parts[1], debugIndex);
        }

        if (parts.Length == 3 && string.Equals(parts[0], "equip", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseZeroBasedInt(parts[1], out int slot) ||
                !TryParseZeroBasedInt(parts[2], out int poolIndex))
            {
                return DeveloperConsoleCommandResult.Error(EngravingUsage);
            }

            return _executor.ExecuteEngravingEquip(slot, poolIndex);
        }

        if (parts.Length == 2 && string.Equals(parts[0], "unequip", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseZeroBasedInt(parts[1], out int slot))
                return DeveloperConsoleCommandResult.Error(EngravingUsage);

            return _executor.ExecuteEngravingUnequip(slot);
        }

        return DeveloperConsoleCommandResult.Error(EngravingUsage);
    }

    private static bool TryReadFloorArguments(string arguments, out string subCommand, out string valueText)
    {
        subCommand = string.Empty;
        valueText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        string trimmed = arguments.Trim();
        int separatorIndex = trimmed.IndexOf(' ');
        subCommand = separatorIndex >= 0 ? trimmed.Substring(0, separatorIndex) : trimmed;
        valueText = separatorIndex >= 0 ? trimmed.Substring(separatorIndex + 1).Trim() : string.Empty;

        if (valueText.IndexOf(' ') >= 0)
            return false;

        return !string.IsNullOrWhiteSpace(subCommand);
    }

    private static bool TryReadGiveArguments(
        string arguments,
        out string categoryToken,
        out string itemCode,
        out string countText)
    {
        categoryToken = string.Empty;
        itemCode = string.Empty;
        countText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        string[] parts = arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 3)
            return false;

        categoryToken = parts[0];
        itemCode = parts[1];
        if (parts.Length == 3)
            countText = parts[2];

        return !string.IsNullOrWhiteSpace(categoryToken) && !string.IsNullOrWhiteSpace(itemCode);
    }

    private static bool TryReadEnhanceArguments(
        string arguments,
        out string formToken,
        out string statToken,
        out string countText)
    {
        formToken = string.Empty;
        statToken = string.Empty;
        countText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        string[] parts = arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 3)
            return false;

        formToken = parts[0];
        statToken = parts[1];
        if (parts.Length == 3)
            countText = parts[2];

        return !string.IsNullOrWhiteSpace(formToken) && !string.IsNullOrWhiteSpace(statToken);
    }

    private static bool TryParsePositiveOptionalCount(string valueText, out int count)
    {
        if (string.IsNullOrWhiteSpace(valueText))
        {
            count = 1;
            return true;
        }

        return TryParsePositiveInt(valueText, out count);
    }

    private static bool TryParsePositiveInt(string valueText, out int value)
    {
        if (!int.TryParse(valueText, out value))
            return false;

        return value > 0;
    }

    private static bool TryParseZeroBasedInt(string valueText, out int value)
    {
        if (!int.TryParse(valueText, out value))
            return false;

        return value >= 0;
    }

    private static string GetFloorUsage()
        => "Usage: /floor add [count] | /floor sub [count] | /floor set [floor]";

    private static string GetEnhanceStatUsage()
        => EnhanceUsage + ". Valid stats: " + string.Join(", ", DeveloperConsoleSoulStatResolver.StatTokens);
}

internal static class DeveloperConsoleItemCategoryResolver
{
    private static readonly ItemType[] s_Types =
    {
        ItemType.Soul,
        ItemType.Relic,
        ItemType.Consumable,
        ItemType.Currency,
        ItemType.Material,
        ItemType.Key,
        ItemType.Equipment
    };

    private static readonly string[] s_Tokens = BuildTokens();

    public static IReadOnlyList<string> CategoryTokens => s_Tokens;

    public static bool TryResolveCategory(string token, out ItemType type)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            for (int i = 0; i < s_Tokens.Length; i++)
            {
                if (string.Equals(token, s_Tokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    type = s_Types[i];
                    return true;
                }
            }
        }

        type = default;
        return false;
    }

    public static string GetCategoryToken(ItemType type)
    {
        for (int i = 0; i < s_Types.Length; i++)
        {
            if (s_Types[i] == type)
                return s_Tokens[i];
        }

        return type.ToString().ToLowerInvariant();
    }

    private static string[] BuildTokens()
    {
        string[] tokens = new string[s_Types.Length];
        for (int i = 0; i < s_Types.Length; i++)
            tokens[i] = s_Types[i].ToString().ToLowerInvariant();

        return tokens;
    }
}

internal static class DeveloperConsoleSoulStatResolver
{
    private static readonly SoulStatType[] s_Stats =
    {
        SoulStatType.AttackSpeed,
        SoulStatType.CooldownReduction,
        SoulStatType.Crit,
        SoulStatType.Lifesteal,
        SoulStatType.MagazineSize,
        SoulStatType.ReloadSpeed,
        SoulStatType.ParryStackMax,
        SoulStatType.ParryGrace,
        SoulStatType.ComboDamage,
        SoulStatType.AilmentDamage
    };

    private static readonly string[] s_Tokens = BuildTokens();

    public static IReadOnlyList<string> StatTokens => s_Tokens;

    public static bool TryResolve(string token, out SoulStatType stat)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            for (int i = 0; i < s_Tokens.Length; i++)
            {
                if (string.Equals(token, s_Tokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    stat = s_Stats[i];
                    return true;
                }
            }
        }

        stat = default;
        return false;
    }

    public static string GetToken(SoulStatType stat)
    {
        for (int i = 0; i < s_Stats.Length; i++)
        {
            if (s_Stats[i] == stat)
                return s_Tokens[i];
        }

        return stat.ToString().ToLowerInvariant();
    }

    private static string[] BuildTokens()
    {
        string[] tokens = new string[s_Stats.Length];
        for (int i = 0; i < s_Stats.Length; i++)
            tokens[i] = s_Stats[i].ToString().ToLowerInvariant();

        return tokens;
    }
}

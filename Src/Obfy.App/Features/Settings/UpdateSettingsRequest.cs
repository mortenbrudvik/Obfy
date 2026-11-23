using System.Text.Json;
using Mediator;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.App.Features.Settings;

/// <summary>
/// Changes some settings (<c>settings.update</c>). <paramref name="Patch"/> maps <c>id.name</c> keys, as in the snapshot's
/// techniques, to new values; see <see cref="ProtectionSession.Update"/>. Refused with <c>run.blocked</c> while a run goes. Answers with the new snapshot.
/// </summary>
public record UpdateSettingsRequest(Dictionary<string, JsonElement> Patch) : IRequest<SettingsSnapshot>;

public class UpdateSettingsRequestHandler(ProtectionSession session, RunCoordinator run) : IRequestHandler<UpdateSettingsRequest, SettingsSnapshot>
{
    public ValueTask<SettingsSnapshot> Handle(UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request.Patch is null)
            throw new ObfyApiException("settings.invalidPatch", "Settings patch must be an object.");

        run.WhileIdle(() =>
        {
            var patch = new Dictionary<string, object?>(request.Patch.Count);
            foreach (var (key, value) in request.Patch)
                patch[key] = ToValue(key, value);

            session.Update(patch);
        });
        return new ValueTask<SettingsSnapshot>(session.GetSnapshot());
    }

    /// <summary>The CLR value a JSON value stands for: bool, whole number, text, a list of text, or null.</summary>
    private static object? ToValue(string key, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? whole : value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : throw new ObfyApiException("settings.invalidValue", $"{key}: expected a list of text"))
            .ToList(),
        _ => throw new ObfyApiException("settings.invalidValue", $"{key}: expected a simple value")
    };
}

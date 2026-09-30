using System;

namespace MackySoft.Navigathena
{

    /// <summary>Describes the current physical realization associated with one logical entry.</summary>
    public sealed record PresentationState (NavigationEntryId EntryId, PresentationMaterialization Materialization, PresentationId? Id, NavigationIncidentId? IncidentId)
    {
        internal static PresentationState Dormant (NavigationEntryId entryId) => new(entryId, PresentationMaterialization.Dormant, null, null);
        internal static PresentationState Available (NavigationEntryId entryId) => new(entryId, PresentationMaterialization.Available, new PresentationId(Guid.NewGuid()), null);
        internal static PresentationState Lost (NavigationEntryId entryId, PresentationId? id, NavigationIncidentId incidentId) => new(entryId, PresentationMaterialization.Lost, id, incidentId);
    }

}

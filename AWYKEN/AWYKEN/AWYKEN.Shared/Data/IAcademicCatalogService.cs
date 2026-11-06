namespace AWYKEN.Shared.Data;

/// <summary>
/// Admin-owned academic reference data: terms, subjects, and class offerings. Every method
/// returns <c>null</c> on success or a human-readable message to show in the admin panel.
/// </summary>
public interface IAcademicCatalogService
{
    Task<IReadOnlyList<AcademicTermView>> GetTermsAsync(bool includeArchived = true,
        CancellationToken cancellationToken = default);
    Task<string?> SaveTermAsync(AcademicTermRecord term, CancellationToken cancellationToken = default);
    Task<string?> SetCurrentTermAsync(int termId, CancellationToken cancellationToken = default);
    Task<string?> ArchiveTermAsync(int termId, bool archived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubjectView>> GetSubjectsAsync(bool includeArchived = true,
        CancellationToken cancellationToken = default);
    Task<string?> SaveSubjectAsync(SubjectRecord subject, CancellationToken cancellationToken = default);
    Task<string?> ArchiveSubjectAsync(int subjectId, bool archived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassOfferingView>> GetOfferingsAsync(int? termId = null, bool includeArchived = true,
        CancellationToken cancellationToken = default);
    Task<string?> SaveOfferingAsync(ClassOfferingRecord offering, CancellationToken cancellationToken = default);
    Task<string?> ArchiveOfferingAsync(int offeringId, bool archived, CancellationToken cancellationToken = default);
}

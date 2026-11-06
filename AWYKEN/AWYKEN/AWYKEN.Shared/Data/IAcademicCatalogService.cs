namespace AWYKEN.Shared.Data;

/// <summary>
/// Admin-owned academic reference data: terms, subjects, and class offerings, plus the
/// bridge to the students' own schedule rows. Every method returns <c>null</c> on success or
/// a human-readable message to show in the admin panel.
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
    Task<string?> SaveOfferingAsync(ClassOfferingRecord offering, bool applyToLinkedStudents = false,
        CancellationToken cancellationToken = default);
    Task<string?> ArchiveOfferingAsync(int offeringId, bool archived, CancellationToken cancellationToken = default);

    /// <summary>How many student rows are linked to this offering, and how many already differ.</summary>
    Task<(int Linked, int Diverged)> GetOfferingImpactAsync(int offeringId,
        CancellationToken cancellationToken = default);

    /// <summary>Distinct courses gathered from the students' own schedule rows.</summary>
    Task<IReadOnlyList<CollectedCourseView>> GetCollectedCoursesAsync(int? termId = null,
        bool onlyUnmatched = false, CancellationToken cancellationToken = default);

    /// <summary>Creates the subject and class for a gathered course. Safe to call twice.</summary>
    Task<string?> PromoteCourseAsync(CollectedCourseView course, int termId,
        CancellationToken cancellationToken = default);

    /// <summary>Points the gathered rows at their class and copies the official values onto them.</summary>
    Task<string?> LinkCourseAsync(CollectedCourseView course, int? termId,
        CancellationToken cancellationToken = default);
}

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// The model of the shared <c>_ProfileHeader</c> partial: the header data plus the tab whose link is marked current.
/// </summary>
/// <param name="Header">The profile header loaded for this request.</param>
/// <param name="ActiveTab">The profile page being shown.</param>
public sealed record class ProfileHeaderView(ProfileHeader Header, ProfileTab ActiveTab);

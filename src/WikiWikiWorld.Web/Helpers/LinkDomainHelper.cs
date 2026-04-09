using System;
using System.Collections.Generic;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Helper class for determining if a given URL belongs to an approved domain.
/// </summary>
public static class LinkDomainHelper
{
	/// <summary>
	/// Checks whether a given URL string points to a domain within the approved domains list.
	/// Internal links (relative paths) automatically return true.
	/// </summary>
	/// <param name="UrlString">The URL to check.</param>
	/// <param name="ApprovedDomains">The collection of approved domains.</param>
	/// <returns>True if the domain is approved or if the link is internal; false otherwise.</returns>
	public static bool IsApprovedDomain(string UrlString, IReadOnlyList<string> ApprovedDomains)
	{
		if (string.IsNullOrWhiteSpace(UrlString))
		{
			return true;
		}

		// Assume internal links (relative) or non-HTTP schemas are not external "unapproved" domains
		if (!UrlString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
			!UrlString.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
			!UrlString.StartsWith("//", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Handle protocol-relative URL
		string ParseableUrl = UrlString;
		if (ParseableUrl.StartsWith("//", StringComparison.OrdinalIgnoreCase))
		{
			ParseableUrl = "https:" + ParseableUrl;
		}

		if (Uri.TryCreate(ParseableUrl, UriKind.Absolute, out Uri? UriResult))
		{
			string Host = UriResult.Host;

			foreach (string Domain in ApprovedDomains)
			{
				if (Host.Equals(Domain, StringComparison.OrdinalIgnoreCase) ||
					Host.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}

		return false;
	}
}

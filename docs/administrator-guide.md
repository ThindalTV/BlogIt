# BlogIt administrator guide

The BlogIt admin portal is included in the host application; it is not deployed
as a separate website. By default it is available at `/blogit/` on the same
origin as the public site. The host can choose another path during application
startup.

## First-run setup

After the application has been installed and its database migrations have run:

1. Open `https://your-site/blogit/`.
2. Create the first administrator account with a username, display name, and
   password. Passwords must be 8 to 128 characters and contain at least one
   uppercase letter, one lowercase letter, and one digit. The same rules apply
   everywhere a password is set.
3. Enter the public site name, canonical site URL, description, and optional
   default Open Graph image URL.
4. Select an AI provider. Enter its API key and, for an OpenAI-compatible
   provider, its base URL and model names. These fields can be left empty if AI
   authoring will not be used yet.
5. Optionally enter the Google Tag Manager container ID — `GTM-` followed by
   letters and digits. This is the container BlogIt loads into every public page,
   and it is where your tags, triggers and cookie consent live. Leave it blank to
   track nothing.
6. Optionally enter the GA4 property ID and service-account credentials JSON,
   which read that traffic back into the dashboard. This step is only available
   once a container ID has been entered on step 5 — see "Tracking and reporting"
   below.
7. Finish setup and sign in.

AI and analytics reporting come from optional packages the host installs
(`BlogIt.OpenAi` and `BlogIt.GoogleAnalytics`). Filling in these settings does
nothing if the corresponding package was not installed — see "Optional features"
below — so check with whoever deployed the application before assuming a
misconfiguration.

Setup can only be completed once. It creates the first user and the JWT signing
secret, saves the site settings, and enables login. Secrets are stored in the
BlogIt database. Protect database backups and restrict access to the admin path.

## Authentication and accounts

The portal signs users in with BlogIt's JWT authentication. Use **My Account**
to change your own password and **Logout** to remove the browser's saved session.
The token lifetime is configured in **Settings** from 5 to 10,080 minutes.

**Users** lists all administrators. You can create users with a username,
display name, and initial password, or delete another user. The currently
signed-in user cannot delete their own account from this screen.

## Dashboard

The dashboard summarizes post and page counts and shows recent content. Use it
as the starting point for editing an existing item or creating the first post.

## Posts

**Posts** manages dated blog entries:

- Create a post with title, optional slug, summary, and Markdown content.
- Add comma-separated tags.
- Use **Insert Media** to add a selected library item as Markdown.
- Set SEO title, description, keywords, and an Open Graph image.
- Save as a draft, publish immediately, or schedule publication and
  unpublication. Times are entered in the browser's local time and stored as
  UTC.
- Preview a saved post without publishing it. Preview links are single-use and
  are marked `noindex,nofollow`.

If the slug is blank, BlogIt derives it from the title. A post slug is locked
after the first publication so existing links remain stable, so a published post
never changes URL. If you do need an old address to keep working — a URL from a
previous site, for instance — add it yourself under **Redirects**.

## Pages

**Pages** manages non-dated content such as About or Contact pages. Pages have a
title, slug, Markdown body, publication state or schedule, preview, and the same
SEO fields as posts. A page slug is locked after its first publication.

The public site's application decides which route renders these pages. Confirm
that the host has a page route using `IPublicContentService.GetPageAsync`.

## Media

**Media** stores images, videos, PDFs and other files. Search by title, select an
item to view its stored name, size, upload date, and public media path, or delete
it. Deleting an item removes both its database record and provider content;
existing links to it will stop working.

Uploads are limited to 50 MB by default. A file over the limit is refused with a
message naming the limit, so the portal can tell you what happened rather than
only that the upload failed. Your developers can change the limit with
`BlogItOptions.MaxMediaUploadBytes`.

Raising it above about 28 MB usually takes more than that one setting. Reverse
proxies and IIS apply their own request size caps, and those reject an upload
before it reaches BlogIt at all — so it fails without the explanatory message. If
large uploads still fail after the limit has been raised, the next place to look
is the proxy or web server in front of the application, not BlogIt.

BlogIt does not restrict which file types can be uploaded, and it serves each file
back with the content type the browser reported. An uploaded HTML file is therefore
served as a web page on your site's own domain, and script inside it runs with the
same privileges as your own pages. This is deliberate — BlogIt treats every
administrator as a fully trusted author — but it means administrator access is
equivalent to trusting someone with the site itself. Grant it accordingly, and see
"Restrict administrator accounts" under Operational notes.

With filesystem storage, operations use the directory configured by the host.
With Azure storage, files remain private in Blob Storage and are served through
BlogIt's public media proxy.

## AI authoring

**AI** stores writing conversations using the provider configured in
**Settings**. Create a conversation, send prompts, and export the result to a
new post draft with optional final instructions. Review and edit every exported
draft before publishing it.

AI is unavailable until valid provider credentials and model settings have been
saved. Leaving a secret field blank in **Settings** preserves the existing
stored value.

## Redirects

**Redirects** maps an old source path to a target path or URL. Choose a permanent
301 redirect for a lasting move or a temporary 302 redirect for a short-lived
change. Every redirect is one you add here; BlogIt never creates one on your
behalf. Avoid redirect loops and do not use a source path already handled by
the application: redirects are matched before the application's own pages, so a
redirect on a path a page already uses hides that page. Your developers can
restrict redirect sources to particular path prefixes; if they have, a source
outside them is refused and the error message lists the prefixes you may use.

## Settings

**Settings** contains:

| Section | Purpose |
| --- | --- |
| Site | Public name, canonical URL, description, and default Open Graph image |
| AI | Provider, API key, endpoint, normal model, and draft-export model |
| Analytics | Google Tag Manager container ID, and — once that is set — the GA4 property ID and service-account JSON |
| Auth | Admin JWT lifetime in minutes |

Use an absolute public **Site URL** without an admin path. It is used by feeds,
sitemaps, canonical links, and SEO metadata. Sensitive AI and Analytics fields
are not returned to the form; leaving them blank keeps their current values.

The **AI** endpoint must be an absolute `http://` or `https://` URL, and by
default it may not point at the machine itself or at a private network address —
your API key is sent to whatever you enter there, so addresses reachable only from
inside the deployment are refused. Leave it blank to use the provider's own
endpoint. If your organisation runs its own model locally, ask your developers to
enable private AI endpoints in the host application; it is not a portal setting.

## Optional features

AI authoring and analytics reporting are separate packages the host application
installs. Whether they are present is a deployment decision, not a portal
setting, so the symptoms of a missing package are worth recognising:

| Feature | Package | If it was not installed |
| --- | --- | --- |
| AI brainstorm and export-to-draft | `BlogIt.OpenAi` | Sending a message or exporting a draft fails with a message naming the package to install. Existing conversations still open and can still be deleted. |
| Dashboard analytics panel | `BlogIt.GoogleAnalytics` | The panel reports that analytics is not configured — the same message it shows when the property ID or credentials JSON are blank. |

The client-side tag is built in and needs no extra package: saving a container ID
starts loading Google Tag Manager even without `BlogIt.GoogleAnalytics`. That
package is only needed to read the reports back into the dashboard.

### Tracking and reporting

The two halves of analytics are one decision, taken in one order:

1. The **container ID** loads Google Tag Manager. Everything that collects
   visitor data is configured inside the container, consent included, so nothing
   is tracked until it is set.
2. The **property ID** and **service-account credentials JSON** are GA4 Data API
   credentials. They only read back what the container collected.

Reporting therefore cannot be configured without a container ID. The setup
wizard keeps them as consecutive steps and hides the reporting step until a
container ID is entered; Settings hides the reporting fields for the same reason,
and the API rejects the combination outright wherever it arrives from.

Clearing the container ID clears the reporting fields with it. There is no state
in which BlogIt holds analytics credentials for a site it is not tracking.

A container ID is not a GA4 measurement ID. `GTM-WQCQZBKK` is a container;
`G-XXXXXXXXXX` is a measurement ID, belongs inside the container as a Google tag,
and is rejected here. If you were using a measurement ID before this changed,
open your GTM container, add your GA4 tag inside it, and enter the container ID
instead — the old value is not carried over.

## Operational notes

- **Run exactly one instance.** BlogIt is single-instance today, and it does not
  detect or warn when it is not. Two instances against the same database will
  disagree: a setting or redirect changed on one takes up to 30 seconds to reach
  the other, preview links only work on the instance that issued them, and the
  publication scheduler runs on every instance at once. Scale up rather than out,
  and choose a deployment mode that stops the old instance before starting the
  new one instead of overlapping them. Your developers can find the detail in the
  technical guide under "Deployment: BlogIt is single-instance today".
- Back up the SQL Server database and the configured media store together.
- Run application migrations as part of every BlogIt deployment before serving
  traffic.
- Serve the site over HTTPS because admin credentials and tokens pass through
  the host application.
- Restrict administrator accounts to people who can publish content and change
  site-wide credentials.
- Verify scheduled publication times after browser or server time-zone changes;
  the portal displays local time but persists UTC.

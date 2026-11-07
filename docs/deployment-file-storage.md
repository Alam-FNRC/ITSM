# File storage on IIS

The service desk keeps two kinds of file outside the database:

| Setting | What it holds | Default if not set |
|---|---|---|
| `FileStorage:BasePath` | Ticket attachments | `uploads` under the site folder |
| `FileStorage:EvidencePath` | Security incident evidence | `evidence` under the site folder |

Leaving the defaults on a server is a mistake: the files would sit inside the site folder, where the next deployment can overwrite them. The app logs a warning at startup when either setting is missing or is not a full path.

## Use shared folders (UNC paths)

Both settings take a UNC path. Tested 1 Nov 2026: upload, download and the evidence fingerprint check all work over UNC, and the database stores the full `\\server\share\...` path.

Set them with the app's other settings on the IIS application pool, as `FileStorage__BasePath` and `FileStorage__EvidencePath` ([deployment-staging.md](deployment-staging.md), step 5). Not in the site's `web.config`: each release replaces it.

```powershell
Set-ItsmSetting 'FileStorage__BasePath' '\\fileserver\ITSM-Files\uploads'
Set-ItsmSetting 'FileStorage__EvidencePath' '\\fileserver\ITSM-Evidence\evidence'
```

- **Use the UNC path, never a mapped drive letter.** IIS application pools do not see drives mapped for a logged-on user.
- **Keep evidence on its own share,** so it can have tighter permissions than ticket attachments.
- **With more than one IIS server,** point every server at the same shares. Files saved through one server are then readable through the others.

## The application pool account

The site's application pool needs access to both shares. With IIS's default pool identity, as in [deployment-staging.md](deployment-staging.md), the pool reaches the network as the web server's computer account (`DOMAIN\SERVERNAME$`): grant that account. If the domain team provides a dedicated service account for the pool instead, preferably a group managed service account (gMSA), grant that account.

## Permissions

The app only creates and reads files; it never deletes, renames or overwrites them (deleting an attachment in the app only hides it). Give the pool account only the rights below, on both the share and NTFS:

- Read and execute (traverse folder, list folder, read data, read attributes)
- Create files / write data
- Create folders / append data
- **No Delete, no "Delete subfolders and files", no Modify or Full control**

On the evidence folder:

- Add an **OWNER RIGHTS** entry with the same limited rights. Otherwise the account owns each file it creates and could change that file's permissions.
- **Give no one else access,** apart from the backup account. Reading evidence through the share would skip the app's access check and its record of who opened what. The security team works through the app only.
- If a stored file is changed anyway, the app detects it: every download re-checks the SHA-256, refuses a changed file and alerts the security team.

## Backups and size limits

- **Back up both shares with the database.** A database restore without the files, or the other way round, leaves broken links.
- **Evidence files are limited to 25 MB.** IIS's default request limit (`maxAllowedContentLength`, 30,000,000 bytes) already allows that, so no `web.config` change is needed. If the evidence limit is ever raised, raise the IIS limit too.

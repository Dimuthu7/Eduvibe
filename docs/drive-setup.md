# Google Drive setup (class materials)

Teachers on a plan with **Drive materials** (Pro) keep notes and past papers in their own Google Drive and share them with their students through GuruBook. This page is the one-time setup the person running the API does, and how the feature behaves.

## What GuruBook asks Google for

- The `drive.file` scope only. GuruBook can see and change only the folders and files it created itself, never the rest of the teacher's Drive. This is Google's narrowest Drive scope and does not need Google's restricted-scope security review.
- `openid` and `email`, to show which Google account is connected.

GuruBook makes a folder called **GuruBook** in the teacher's Drive, with one subfolder per class and, inside it, **Notes**, **Past papers** and **Other**. Inside those the teacher makes their own folders in the app (for example Term 1 / Week 2), up to four levels deep, and they are real folders in Drive too. Adding a file puts it in the folder the teacher picked; changing a file's kind or folder moves it in Drive; renaming or removing an (empty) folder does the same there. Files and folders can be moved with **Move** in the 3-dot menu (to any shelf or folder of the class, a folder takes what is inside it, and moving a folder to another shelf changes the kind of its files) or by dragging a row onto a folder or onto a name in the path above the list (on a phone, press and hold a row for a moment first). Files added before folders existed stay in the class folder and show on their Notes, Past papers or Other shelf. If the teacher deletes a folder in Drive, the next upload or new folder makes it again.

## One-time setup in Google Cloud

1. Open <https://console.cloud.google.com/> and create a project (for example "GuruBook").
2. **APIs & Services > Library**: enable **Google Drive API**.
3. **APIs & Services > OAuth consent screen** (Google Auth platform): choose **External**, fill in the app name, support email and a privacy policy link, and add the scopes `.../auth/drive.file`, `openid` and `email`.
4. While the app is in **Testing**, add each teacher's Google account under **Test users** (up to 100). Press **Publish app** when real teachers start using it; with only the scopes above Google does not require a verification review for the `drive.file` scope, but it can still show an "unverified app" screen until the consent screen is verified.
5. **Credentials > Create credentials > OAuth client ID**, type **Web application**. Under **Authorised redirect URIs** add the API's callback address:
   - Render test site: `https://eduvibe-api-hnd2.onrender.com/api/materials/drive/callback`
   - Local: `http://localhost:5077/api/materials/drive/callback`
6. Copy the client id and secret.

## Settings on the API

| Setting | Meaning |
| --- | --- |
| `Google__ClientId` | The OAuth client id. |
| `Google__ClientSecret` | The OAuth client secret. Keep it out of git. |
| `Google__RedirectUri` | Exactly the callback address added in step 5. |
| `Google__WebReturnUrl` | Optional. Where the browser goes after Google. It defaults to the first `Cors__AllowedOrigins__*` address, which is the web app. |

On Render, `render.yaml` lists these with `sync: false`: set them on the dashboard (Environment). Until the first three are set the Materials screen tells teachers that Drive is not set up yet, and nothing else in the app is affected.

## How it works

- **Connect.** The teacher presses *Connect Google Drive*, agrees on Google's page, and returns to the Materials screen. GuruBook stores one refresh token per teacher, encrypted with a key derived from `Jwt__SigningKey`. If that signing key is ever replaced, stored tokens cannot be read and every teacher is asked to connect again; nothing is lost.
- **Add a file.** PDF, Word, PowerPoint, Excel, text and pictures up to 25 MB. The file goes straight to the chosen folder in the teacher's Drive. GuruBook keeps only its Drive file id, name, type, size, class, kind (notes, past paper, other), folder, topic and year, and the list of the teacher's folders (name, parent, Drive folder id).
- **Students.** A student opens a file through GuruBook (`GET /api/materials/mine/{id}/file`). The API fetches it from the teacher's Drive and streams it, so nothing in Drive is shared with the student and Drive links are never given out. A student sees a file only while they are active in its class, the class is not archived, and the teacher's plan still includes Drive materials.
- **Removing a file** deletes it from the teacher's Drive (if Google cannot be reached it is still removed from the class so students stop seeing it).
- **Disconnecting** tells Google to drop GuruBook's access and removes the stored token. The list of materials is kept, so connecting the same Google account again brings it back. Files stay in the teacher's Drive.
- **If Google stops accepting the link** (the teacher removed GuruBook in their Google account settings), the teacher sees *Google Drive needs to be connected again*; students are told to ask their teacher.

## Gating

The feature key is `drive_materials`: off on **Free**, on for **Pro**. The Super Admin can add it to any plan in **Plans**. A teacher without it sees what it gives and which plan includes it.

## Things to know

- The cloud sandbox this project was built in cannot reach Google, so the live Google calls (`GoogleDriveClient`) are tested only against a fake. The first connection on the test site is the real check: connect, add a small PDF, open it as a student.
- Google refresh tokens for apps in **Testing** expire after 7 days. Publish the app before a pilot, or teachers will be asked to connect again weekly.
- Large files use a resumable upload, streamed from the request to Google, so the API does not hold a file in memory. Render's free tier can still time out a slow upload on a poor connection.

## If something fails

The Materials screen shows Google's reason where it can:

| Message | Cause and fix |
| --- | --- |
| Google Drive is switched off for GuruBook's Google project | The **Google Drive API** is not enabled. In Google Cloud open **APIs & Services > Library**, search "Google Drive API" and press **Enable**, wait a minute, try again. |
| Your Google Drive is full | The teacher's own Drive has no space left. |
| Google Drive could not be reached | Anything else Google refused. The API log has a line starting "Google Drive answered" with the status and Google's reason. |
| Google Drive needs to be connected again | The teacher removed GuruBook in their Google account, or the grant expired (7 days in Testing mode). They press *Connect again*. |
| Connecting says permission is needed | The teacher unticked the Drive permission on Google's page, or their Gmail is not on the **Test users** list while the app is in Testing. |

# WhatsApp setup (messages to parents)

Teachers on a plan with **WhatsApp** (Pro) send fee reminders, absence alerts and exam results to parents from the Fees, Attendance and Exams screens. Messages go out through Meta's **WhatsApp Cloud API** from one GuruBook number, using three message templates that Meta must approve first. This page is the one-time setup the person running the API does, and how the feature behaves.

Nothing here is needed to try the screens: with `WhatsApp__Mode=Simulated` (the test site's setting) every send is accepted and nothing is sent, and the screens say *Test mode*.

## How it behaves

- **Preview, then send.** From a class's fees for a month, a session's attendance or a published exam, the teacher opens the dialog and sees how many messages will go and why the others will not: already messaged, no phone number, asked not to be messaged, or over the month's allowance. The count in the preview is what the send does.
- **Who gets it.** The parent (the guardian's number when the student has one, otherwise the student's own number). Numbers are stored in international form, for example `+94771234567`.
- **Once only.** A fee reminder for the same student and month is not sent again within 7 days; an absence alert is sent once per student per class session; results are sent once per student per exam.
- **Allowance.** The plan's `whatsapp_per_month` limit (Free 0, Pro 500) counts the messages queued in the calendar month (Sri Lanka time). Messages that fail do not count. If a send is bigger than what is left, the first students on the list are messaged and the rest are reported as over the allowance. The Super Admin changes the number per plan in **Plans**.
- **Background sending.** Messages are saved as *Waiting* and sent within seconds by a background job, one at a time. If WhatsApp is busy or unreachable the message is tried again after 2, 4 and 8 minutes, then marked *Failed*. If WhatsApp refuses it for good (the number is not on WhatsApp, the template is missing) it is marked *Failed* straight away with the reason. A restart loses nothing: waiting messages are picked up again.
- **Status.** Meta reports each message as sent, delivered, read or failed to the webhook below, and the **WhatsApp** screen (teacher menu) shows it next to each message with the month's allowance.
- **Stopping.** A parent who replies **STOP** is never messaged again by the teachers who messaged them in the last 90 days, until they reply **START**. This needs the webhook below. Every template ends with *Reply STOP to stop these messages.*
- **Locked.** A teacher without the feature sees what it gives and which plan has it. The API answers `403 plan_feature_locked`.
- **Not set up.** Until the settings below are present (and Mode is not Off) the screens say WhatsApp is not set up and nothing is queued.

## One-time setup with Meta

1. Create a **Meta Business portfolio** at <https://business.facebook.com/> for GuruBook and complete **business verification** (needed to send to anyone beyond a handful of test numbers). Meta asks for the registered business details and a document; allow a few days.
2. At <https://developers.facebook.com/> create an app of type **Business** and add the **WhatsApp** product. Link it to the portfolio.
3. In **WhatsApp > API setup** add the sending phone number. It must not be registered in the ordinary WhatsApp or WhatsApp Business app. Choose a display name Meta will accept (for example *GuruBook*). Note the **Phone number ID** (not the number itself).
4. Create the access token: in **Business settings > Users > System users** add a system user, give it the app and the WhatsApp account, and generate a token with the permissions `whatsapp_business_messaging` and `whatsapp_business_management` and **no expiry**. (The temporary token on the API setup page lasts 24 hours and is for trying only.)
5. Submit the three **message templates** below in **WhatsApp Manager > Message templates**: category **Utility**, language **English**, with exactly these names and texts. Approval usually takes minutes to a day. Templates in other languages (Sinhala, Tamil) can be added later; today every message goes out in the language set by `WhatsApp__Language`.
6. Set the webhook: in **WhatsApp > Configuration** set the callback URL to `https://<your API>/api/whatsapp/webhook` (Render test site: `https://eduvibe-api-hnd2.onrender.com/api/whatsapp/webhook`) and the verify token to the same text as `WhatsApp__VerifyToken`, then subscribe to the **messages** field (delivery reports and STOP replies both arrive through it). Meta calls the address once to check it, so the API must already be running with the settings below.
7. Set the settings below on the API, then change `WhatsApp__Mode` to `Meta`.

## Message templates

Submit these exactly (the `{{n}}` places are filled by GuruBook in this order). Meta asks for example values; the ones shown work.

**`gurubook_fee_reminder`** (5 values)

> Dear parent, this is a reminder about {{1}}'s {{2}} fee for {{3}}: {{4}} is still due to {{5}}. Thank you. Reply STOP to stop these messages.

Examples: Kamal, 2027 A/L Maths, March 2027, LKR 2,000, Mr Perera.

**`gurubook_absence_alert`** (4 values)

> Dear parent, {{1}} was absent from the {{2}} class on {{3}}. This message is from {{4}}. Reply STOP to stop these messages.

Examples: Kamal, 2027 A/L Maths, Mon 15 Mar, Mr Perera.

**`gurubook_results`** (7 values)

> Dear parent, {{1}}'s result for {{2}} ({{3}}): {{4}} marks, grade {{5}}, rank {{6}}. From {{7}}. Reply STOP to stop these messages.

Examples: Kamal, Term test, 2027 A/L Maths, 78 / 100, B, 3 of 24, Mr Perera.

The template names are `WhatsApp__TemplatePrefix` (default `gurubook_`) plus `fee_reminder`, `absence_alert` and `results`. Changing the wording in Meta is fine as long as the number and order of the `{{n}}` places stay the same; adding a place needs a code change.

## Settings on the API

| Setting | Meaning |
| --- | --- |
| `WhatsApp__Mode` | `Off` (default: nothing can be sent), `Simulated` (accepts every send, sends nothing: demos and the test site) or `Meta` (really sends). |
| `WhatsApp__PhoneNumberId` | The sending number's id from step 3. |
| `WhatsApp__AccessToken` | The system user's permanent token from step 4. Keep it out of git. |
| `WhatsApp__VerifyToken` | Any long random text; the same text goes into Meta's webhook setup. |
| `WhatsApp__AppSecret` | The Meta app's secret (**App settings > Basic**). Every webhook call is checked against it, and calls that fail the check are refused. |
| `WhatsApp__Language` | Template language code, default `en`. |
| `WhatsApp__TemplatePrefix` | Default `gurubook_`. |
| `WhatsApp__ApiVersion` | Graph API version, default `v21.0`. |
| `WhatsApp__DispatchJob` | `false` stops the background sender (tests). |

On Render, `render.yaml` lists these: the tokens and secrets use `sync: false`, so set them on the dashboard (Environment). `render.yaml` ships `WhatsApp__Mode=Simulated`; change it to `Meta` only once steps 1 to 6 are done, otherwise teachers would see *Sent* for messages nobody receives.

## Cost

Meta charges per delivered template message by category and country. Utility messages to Sri Lanka were about US$0.0023 each when this was written (see Meta's rate card for today's price); the first 1,000 service conversations a month are free but template messages are not. At 500 messages a month a Pro teacher costs about US$1.15. Marketing messages are not used.

## Gating

The feature key is `whatsapp` and the limit is `whatsapp_per_month` (Free: off and 0, Pro: on and 500). Sending is also refused by the API itself, not only hidden in the app.

## Things to know

- The cloud sandbox this project was built in cannot reach Meta, so `MetaWhatsAppClient` is tested only against a fake. The first real check is on the test site: switch to `Meta` with a test number added in step 3, send yourself a reminder, and confirm *Delivered* appears (that proves the webhook too).
- Meta only lets an unverified business message a few numbers you list as testers. Real parents need step 1.
- Password reset by WhatsApp and marketing messages are not part of this release.

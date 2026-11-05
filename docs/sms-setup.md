# SMS setup (text messages to parents)

Teachers on a plan with **Text messages (SMS)** (Pro) reach the parents who are not on WhatsApp. The same Fees, Attendance and Exams buttons send them, and the words are short on purpose because every message part is paid for in cash. Texts go out through **text.lk**, a Sri Lankan provider, from one approved sender name. This page is the one-time setup the person running the API does, and how the feature behaves.

Nothing here is needed to try the screens: with `Sms__Mode=Simulated` (the test site's setting) every send is accepted and nothing is sent, and the screens say *Test mode*.

## How it behaves

- **WhatsApp first.** Each teacher chooses on the **Text messages** screen how their paid messages go: *WhatsApp, then SMS* (the default) or *SMS only*. On the default, a message is sent on WhatsApp and only a parent WhatsApp will not deliver to (not on WhatsApp, not delivered) gets a text instead; the WhatsApp screen says *Sent as a text instead* on that message. On *SMS only* nothing goes to WhatsApp at all, and the send dialog says *These go as text messages*.
- **Preview, then send.** The same dialog as WhatsApp, with the SMS allowance: how many texts will go and why the others will not (already sent, no phone number, asked not to be texted, over the month's allowance).
- **Who gets it.** The parent (the guardian's number when the student has one, otherwise the student's own number), in international form, for example `+94771234567`.
- **The words.** Written by GuruBook in the teacher's own language (English, Sinhala or Tamil) and kept to one short line, because a phone fits 160 English letters in one paid part but only 70 Sinhala or Tamil ones. They are in `SmsTexts.cs`, the one place in the API that holds words a parent reads. The very first text a number ever gets ends with *To stop: ask your teacher.*; later texts leave it off so they stay cheap.
- **Allowance in parts, not texts.** The plan's `sms_per_month` limit (Free 0, Pro 300) counts the **parts** queued in the calendar month (Sri Lanka time), because that is what the provider bills. A Sinhala reminder is usually two parts. Texts that fail do not count. If a send is bigger than what is left, the first students on the list are texted and the rest are reported as over the allowance. The Super Admin changes the number per plan in **Plans**.
- **Once only.** The same dedup keys as WhatsApp: a fee reminder for one student and month is not sent again within 7 days, an absence alert once per student per session, results once per student per exam. A message that went by WhatsApp is not also sent as a text unless WhatsApp failed to deliver it.
- **Background sending.** Texts are saved as *Waiting to send* and sent within seconds, one at a time: pressing Send starts a run straight away, a background loop also checks every 30 seconds, and opening the Text messages screen nudges it again while anything is waiting. The API log says `SMS sender started in <Mode> mode` on start and `SMS <id> sent (<n> part(s))` or `failed: <code>` for each one. If the provider is busy or unreachable the text is tried again after 2, 4 and 8 minutes, then marked *Failed*. **A refused text is never tried again**, because every attempt is paid for. A restart loses nothing.
- **Stopping.** A parent or student turns texts off themselves under their own profile (*Send me text messages*), or asks their teacher, who presses **Stop texts to this number** beside any text they sent. Unlike WhatsApp's STOP, this covers every teacher at once: a text arrives with no way to reply to the right one. In-app notifications are unaffected.
- **Locked.** A teacher without the feature sees what it gives and which plan has it. The API answers `403 plan_feature_locked`.
- **Not set up.** Until the settings below are present (and Mode is not Off) the screens say text messages are not set up and nothing is queued.

## One-time setup with text.lk

1. Create an account at <https://text.lk/> and top up. Prices were about Rs 0.64 to 0.84 per part including tax when this was written; check their price list for today's.
2. Ask for a **Sender ID** (the name shown on the parent's phone, for example `GuruBook`). Sri Lankan operators have to approve it, which takes a few days and may need the business registration. Until then only their shared sender name works.
3. In the dashboard create an **API token** (Settings or Developers, depending on their current dashboard).
4. Set the settings below on the API, then change `Sms__Mode` to `TextLk`.
5. Send yourself one: on a Pro teacher's account choose *SMS only*, remind one class whose guardian number is your own phone, and confirm the text arrives and the screen says *Sent*.

Another provider (Dialog, Mobitel, Notify.lk) is a second class behind `ISmsClient` and no change anywhere else.

## Settings on the API

| Setting | Meaning |
| --- | --- |
| `Sms__Mode` | `Off` (default: nothing can be sent), `Simulated` (accepts every send, sends nothing: demos and the test site) or `TextLk` (really sends, and really costs money). |
| `Sms__ApiToken` | The API token from step 3. Keep it out of git. |
| `Sms__SenderId` | The approved sender name from step 2, default `GuruBook`. |
| `Sms__BaseUrl` | Default `https://app.text.lk`. |
| `Sms__DispatchJob` | `false` stops the background sender (tests). |

On Render, `render.yaml` lists these: the token uses `sync: false`, so set it on the dashboard (Environment). `render.yaml` ships `Sms__Mode=Simulated`; change it to `TextLk` only once steps 1 to 3 are done, otherwise teachers would see *Sent* for texts nobody receives.

## Cost

A part is paid for whether or not it is read, so SMS is the expensive channel: at Rs 0.80 a part, a Pro teacher's 300 parts are about Rs 240 a month, where 500 WhatsApp messages are about Rs 350 in total but each is one message rather than two parts. That is why WhatsApp is tried first and why the texts are short. Raise `sms_per_month` for a teacher only when they understand the bill.

## Gating

The feature key is `sms` and the limit is `sms_per_month` (Free: off and 0, Pro: on and 300). The send buttons on Fees, Attendance and Exams need `whatsapp` **or** `sms`, so a teacher may pay for either channel alone. Sending is refused by the API itself, not only hidden in the app.

## Things to know

- The cloud sandbox this project was built in cannot reach text.lk, so `TextLkSmsClient` is tested only against a fake, and its reading of text.lk's answer (`status`, `data.uid`) is written from their published API. Step 5 is the first real check; if the text arrives but the screen still says *Waiting to send*, the answer shape is what to look at first.
- Delivery reports are not read yet: a text that the provider accepted shows *Sent*, never *Delivered*. text.lk can report delivery, and that is a later sprint.
- A teacher who chose *SMS only* and has no `whatsapp` feature still sends from the same buttons; the dialog's words follow the channel.
- Replies are not read, so a parent cannot stop texts by answering one. The profile switch and asking the teacher are the two ways, and the first text says so.
- Editing the wording, per-event channel choices and automatic fee reminders by text are Sprint 19 (`custom_messages`), not this release.

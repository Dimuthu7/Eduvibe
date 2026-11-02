# Phone notifications and fee reminders

Everything that shows in the bell (new join requests, results, payments, fee reminders) can also arrive as a notification on the phone, even when GuruBook is closed. This page is the one-time setup the person running the API does, and how the feature behaves.

## One-time setup: the sender keys

Phone notifications use web push, which signs every message with a key pair that belongs to GuruBook (called VAPID keys). Make a pair once, on any computer with Node:

```
node -e "const {generateKeyPairSync}=require('crypto');const k=generateKeyPairSync('ec',{namedCurve:'P-256'}).privateKey.export({format:'jwk'});const b=s=>Buffer.from(s,'base64url');console.log('Push__PublicKey='+Buffer.concat([Buffer.from([4]),b(k.x),b(k.y)]).toString('base64url'));console.log('Push__PrivateKey='+k.d)"
```

Put the two lines in the API's settings (Render: **Environment**). Keep the private key out of git. Do not change the pair later: every phone is tied to the public key it subscribed with, so a new pair switches all phones off until each person turns notifications on again.

| Setting | Meaning |
| --- | --- |
| `Push__PublicKey` | The public key (87 characters). The app uses it to subscribe a phone. |
| `Push__PrivateKey` | The private key (43 characters). Only the API has it. |
| `Push__Subject` | Optional. A website or `mailto:` address the push services can use to reach whoever runs GuruBook. It defaults to the web app's address. |

Without both keys the app does not offer phone notifications, and everything else works as before.

## How a person turns it on

- A card on the **Notifications** screen (and on a student's or parent's home screen) says "Get fee reminders on your phone". Nothing is asked when the app first opens; the browser's own permission question appears only when the person presses **Turn on**.
- Android and computers: works in the browser. iPhone and iPad (iOS 16.4 or newer): GuruBook must first be added to the Home Screen (Share, then Add to Home Screen); the card says so.
- Each phone is stored once, against the person signed in on it. Signing in as someone else on the same phone moves it to them. A phone the push service reports as gone is forgotten.
- Notifications show in the language the person has chosen (Sinhala, Tamil or English).

## Automatic fee reminders

A student gets a reminder:

1. the day before a fee's period starts ("Tomorrow: LKR 2,500 for November, pay by 7 Nov");
2. on the fee's due date;
3. every week after the due date while it is unpaid (a teacher can limit this to 1 to 8 reminders per class; the default is every week until paid, for up to 26 weeks).

Each step is sent once per fee. Reminders stop when the fee is paid, becomes a free card, the student leaves the class or the class is archived. Nothing is sent between 8 PM and 7 AM Sri Lanka time; the hourly check sends what is due at the next run after 7 AM.

On the last day of a month the API also makes the next month's fees, so the "pay tomorrow" reminder has something to point at.

A teacher chooses, per class, **Fee reminders** from the class menu: on or off, and how many weekly reminders. On a student's fee page the teacher can switch reminders off for that student and sees a short log of what was sent and when.

Not yet: reminders to parents (Sprint 16), and WhatsApp or SMS copies of a reminder (Sprints 17 and 19). Free and Pro teachers get the same in-app and phone reminders.

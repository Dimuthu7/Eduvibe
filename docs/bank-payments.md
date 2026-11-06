# Bank payments (pay by transfer or LankaQR)

Teachers on a plan with **Pay by bank transfer** (Pro) let their students and the parents they linked pay a fee from a bank app instead of handing over cash. The teacher puts up their own account details and their LankaQR once; the payer sees what to pay, sends it, and says so with a slip photo or the reference their bank gave; the teacher confirms it, which records an ordinary bank transfer payment with a receipt.

**GuruBook never holds or moves the money.** It carries the teacher's account details, the payer's word that they sent it, and the teacher's word that it arrived. There is nothing to set up with a bank or a payment provider, no fees to GuruBook, and no settings on the API: a teacher can switch this on themselves the moment their plan allows it. Taking card payments online, where money does pass through a provider, is a later sprint.

Free teachers keep recording cash payments by hand on the fees screen, as before.

## How it behaves

- **The teacher's account.** On **Bank payments** the teacher fills in the bank, the account name, the account number, and optionally the branch and a note for the payer. They can add a photo or screenshot of their LankaQR code. Only their own students and the parents they linked ever see any of it; another teacher never does.
- **A short reference.** Every fee gets an eight-character code such as `GB7K2M9Q`, worked out from the enrolment and the month, so the same fee always shows the same code and no two fees share one. The payer is asked to write it on the transfer, and the teacher's list shows it beside the claim, so a line in a bank statement can be matched to one student and one month. Letters that are easily confused by hand (I, L, O, U) are left out.
- **The Pay button.** A student or parent sees **Pay** beside a fee that is late, due today or due soon, but only where that fee's teacher takes transfers and has filled their details in: nobody is offered a dead end. The pay screen shows the amount still owed, the code, the account with copy buttons, and the QR.
- **"I've paid".** The payer enters what they sent, may add their own reference or a note, and may attach a photo of the slip. Nothing is recorded against the fee: the claim only tells the teacher money is on the way, and the teacher is notified in the app.
- **One at a time.** A fee can have only one claim waiting. A second one is refused while the first is undecided, so a teacher is never asked about the same fee twice.
- **Part payments.** The claim can be for less than the fee, but never for more than is still owed.
- **Confirming.** **Confirm** records a payment of method *Bank transfer* for that month, with the teacher's next receipt number, and tells the student and the parents exactly as cash taken by hand does. The teacher can confirm a different amount from the one claimed, which is what happens when a little less actually arrived.
- **Rejecting.** **Reject** asks for a reason and tells the payer, who reads it and can try again. Nothing is recorded against the fee, so it is still owed.
- **Locked.** A teacher without the feature sees what it gives and which plan has it; the API answers `403 plan_feature_locked`. A student whose teacher does not have it is told the fee is taken in person.

## Privacy and the slip photos

A bank slip is the payer's own document, so it is kept no longer than it is useful:

- Slips are stored in the database, like student photos, and are never put on a public address: a teacher reads only their own students' slips, and the payer only their own.
- A slip image is **cleared 120 days after the teacher decides** on the claim, by a background job that runs twice a day (turn it off with `Payments:SlipCleanupJob=false`). The claim itself stays, with the amount, the reference, the decision and the receipt it became, so the record of what was said survives; only the photo goes.
- Slip bytes are kept out of the audit log on purpose, so the log records the decision without carrying copies of bank documents.
- A claim is part of the student's data export and is deleted, image and all, when a student is erased. A teacher's bank details, their QR and their claims go with the teacher when the teacher is erased. See [Privacy requests](privacy.md).
- Nothing about a bank account is sent to any other service. The details only ever travel from the teacher's screen to their own students' screens.

## Things to know

- A LankaQR code is a photo the teacher supplies, not one GuruBook generates; whatever their bank gave them is what the payer scans. The LankaQR merchant discount rate is waived for payments up to Rs 5,000 from 6 April 2026, which covers most monthly fees.
- The teacher is the only check that money actually arrived. A payer who claims a payment that never came simply has it rejected, and the fee stays owed.
- Images are taken as JPEG, PNG or WebP up to 2 MB, for both slips and the QR.
- There is no automatic matching against a bank statement, and no way yet for a teacher to export the claims; both would be worth adding once real teachers have used it.

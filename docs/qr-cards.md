# QR student cards

A teacher prints a card with a QR code for each student, hands them out, and scans them at the start of class to mark attendance.

## How a teacher uses it

1. **Print:** on a class (three dots, **Student cards**) or on the attendance screen (the card icon). Ten cards to an A4 page. Share the PDF or save it, print, cut out. One card for one student: the student's page, **QR card**, **Print card**.
2. **Scan:** open the day's attendance for the class and press **Scan cards**. Hold a card in front of the camera. The student's name appears and they are counted as here. A card of another class or another teacher is refused with a message.
3. **Finish:** press **Done**. Scanned students are marked present. Press **Mark the rest absent** if only the students who came were scanned, then **Save attendance** as usual. Nothing is saved until Save.
4. **Lost card:** the student's page, **Replace card**. The old card stops working straight away.

## How it works

- Each teacher has their own card (and code) for each student, in `students.student_cards`. A code is 16 random bytes in URL-safe text. The QR text is `EV1:` followed by the code, so a scanner that reads some other QR code can say "not a student card" without asking the server.
- `POST /api/attendance/sessions/{id}/scan {code}` finds the student in that session's class and returns their name and enrolment. It saves nothing. A code that is not one of the teacher's cards gets `card_unknown` (404); a card of a student who is not in that class gets `card_not_in_class` (409).
- The Attendance module reaches cards only through `IStudentCards` in Shared (the Students module implements it), like every other cross-module call.
- The PDF is drawn in the browser (canvas, then a small PDF writer), so Sinhala and Tamil names print correctly. The QR encoder (`qrcode-generator`) and the scanner library (`jsqr`) are loaded only when a teacher prints or scans, so the first screen is no heavier.

## Phones and cameras

The camera needs a secure address: `https://` (the test site and any real deployment) or `localhost`. The first time, the browser asks for permission; if it was refused, the scanner says how to allow it. Phones with a built-in QR reader (Chrome on Android) use it; others (iPhone Safari, older browsers) decode the picture in the app. Both read printed cards held about a hand's width from the camera in normal light.

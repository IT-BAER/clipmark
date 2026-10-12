# Clipmark Privacy Policy

Last updated: 12 October 2026

Clipmark is a screen capture, annotation and screen recording app for Windows, made by IT-BAER. This policy explains what Clipmark does with your data. Short version: Clipmark works offline, has no account, and sends nothing to IT-BAER. The one exception to "offline" is the optional Ask AI feature: when you ask a question, Clipmark sends the capture and your question to Anthropic under your own Anthropic API key.

## Who is responsible

Bruno Miglar (IT-BAER)\
Business address: Sankt Veiter Straße 16, 9020 Klagenfurt, Austria\
Email: admin@it-baer.net

## What the app does on your PC

Clipmark makes no network connections, except for Ask AI (described below) and only when you use it. There is no analytics, no tracking, no advertising and no crash reporting built into the app.

Everything below stays on your PC:

- **Screenshots and recordings.** Clipmark captures the screen area you choose, only when you start a capture or a recording. Screenshots go to the clipboard and, if you turn on automatic saving, to your Pictures\Screenshots folder (or the folder you pick). Recordings are saved to Videos\Screen Recordings. You decide where files go and whom you share them with.
- **Microphone and system audio.** Clipmark records audio only while a screen recording runs and only if you turned on "Record the microphone" or "Record system audio" in Settings. Windows asks for your permission before Clipmark can use the microphone.
- **Text recognition and QR codes.** Copying text from a capture uses the text recognition built into Windows, and QR codes are read by a library inside Clipmark. Both run on your PC. No image is uploaded.
- **Links.** Clipmark opens a web page only when you click a link: Buy Clipmark Premium, Source code and releases (GitHub), or Open link next to a QR code that contains a web address. Your browser then loads that page, and the website's own privacy policy applies.
- **Sharing.** When you click Share, Windows opens its own share window and you choose the target app. Clipmark does not send the file anywhere by itself.
- **Settings.** Your settings are stored in the app's local storage on your PC.
- **Capture history (optional, off by default).** If you turn it on, Clipmark keeps your last 25 captures, including your annotations, in the app's local storage on your PC. Each item is encrypted for your Windows account. The history never leaves your PC. Turning the history off deletes it.
- **License and trial.** Clipmark stores your license key and the start of your 30-day trial on your PC. The license key contains only a license number, the issue date and the product; no name and no email address. To keep the trial fair, Clipmark also stores a small trial record (the trial start and the last time the app ran) in Windows Credential Manager. Each trial copy carries a check value computed from these dates and an ID that Windows provides per publisher for this device; the ID itself is not stored. This record contains no personal data, never leaves your PC, and stays on the PC after you uninstall Clipmark so that a reinstall does not restart the trial. You can delete it in Windows Credential Manager under "Windows Credentials", entry "Clipmark/Trial".

When you uninstall Clipmark, Windows deletes the app's local storage (settings, capture history, license key and the trial copies stored there). Your screenshots and recordings stay where they were saved. The trial record in Credential Manager stays as described above, and so does an Ask AI key you did not remove (see below).

## Ask AI (optional)

Ask AI lets you ask Claude, Anthropic's AI model, questions about a capture. It is off until you add your own Anthropic API key in Settings. Clipmark has no Anthropic account of its own and IT-BAER receives nothing.

- **What is sent.** The capture as shown in the editor, including your annotations; redactions are burned in, so the hidden pixels are not sent. Large captures are scaled down to at most 2576 pixels on the long side. With the image go your question and the earlier questions and answers of the same conversation.
- **When.** Only when you send a question. The first time, Clipmark asks you to agree. "Test key" in Settings sends only a request that lists the available models, without any image.
- **Where.** Directly from your PC over HTTPS to Anthropic's API (api.anthropic.com), using your API key. Anthropic processes the data as your API provider under the terms of your Anthropic account (https://www.anthropic.com/legal/commercial-terms) and bills your account for each request. Anthropic's privacy policy: https://www.anthropic.com/legal/privacy.
- **What Clipmark keeps.** The conversation exists only in memory and ends when you start a new chat, open or take another capture, or close the capture. It is not written to disk and is not part of the capture history.
- **Your API key.** Stored in Windows Credential Manager for your Windows account on this PC (entry "Clipmark/AnthropicKey", not synced to other PCs), never in the settings file. Other apps running under your Windows account can read entries in Credential Manager. "Remove key" in Settings deletes it, and so does `Clipmark.App.exe --restore-system`; you can also delete the entry in Credential Manager under "Windows Credentials".

## Buying a license

The Buy button opens it-baer.net/clipmark in your browser; the website's privacy policy is at it-baer.net/legal/datenschutz. Payment is handled by Stripe through its Link checkout (Stripe Managed Payments). Link acts as the merchant of record: it sells you the license, collects the payment and the VAT, and sends the receipt. Stripe has its own privacy policy: https://link.com/privacy. IT-BAER receives from Stripe the data needed to send your license key and to meet tax and accounting duties: your name, email address, country, the order and the amount. IT-BAER does not receive your full card number or bank account details.

IT-BAER uses this data to deliver your license key, to answer support requests and to keep accounting records as required by Austrian law (generally 7 years). Legal basis: performance of the purchase contract (Art. 6(1)(b) GDPR) and legal obligations (Art. 6(1)(c) GDPR).

## Microsoft Store

If you install Clipmark from the Microsoft Store, Microsoft processes data about the download, installation, updates and app health under the Microsoft Privacy Statement. IT-BAER can see aggregated reports in Microsoft Partner Center, such as install counts and crash reports, but no data that identifies you.

## Email

If you email IT-BAER, your message and email address are used only to answer you and are deleted when they are no longer needed, unless the law requires keeping them.

## Children

Clipmark is not directed at children and does not knowingly collect data from anyone.

## Your rights

Under the GDPR you have the right to access, correct and delete your personal data, to restrict or object to its processing, and to data portability. To use these rights, email admin@it-baer.net. You can also complain to a data protection authority, in Austria the Datenschutzbehörde (dsb.gv.at).

## Changes

If this policy changes, the new version is published on this page with a new date.

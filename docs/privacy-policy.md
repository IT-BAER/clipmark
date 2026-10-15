# Clipmark Privacy Policy

Last updated: 14 October 2026

Clipmark is a screen capture, annotation and screen recording app for Windows, made by IT-BAER. This policy explains what Clipmark does with your data. Short version: Clipmark works offline and has no account. It contacts IT-BAER's licence server (releash.it-baer.net) to start your trial, to activate a license and to renew it about every two weeks; these calls carry a key that identifies this PC's Clipmark installation, never your captures, your name or your email address. The optional Ask AI feature sends the capture and your question to Anthropic, either directly under your own Anthropic API key or, for Premium without an own key, through IT-BAER's relay.

## Who is responsible

Bruno Miglar (IT-BAER)\
Business address: Sankt Veiter Straße 16, 9020 Klagenfurt, Austria\
Email: admin@it-baer.net

## What the app does on your PC

Clipmark makes no network connections except the license calls and Ask AI, both described below. There is no analytics, no tracking, no advertising and no crash reporting built into the app.

Everything below stays on your PC:

- **Screenshots and recordings.** Clipmark captures the screen area you choose, only when you start a capture or a recording. Screenshots go to the clipboard and, if you turn on automatic saving, to your Pictures\Screenshots folder (or the folder you pick). Recordings are saved to Videos\Screen Recordings. You decide where files go and whom you share them with. Share, Print and Edit in Paint write a PNG copy to a Clipmark folder in your Windows temp folder and hand it to that app; Clipmark deletes these copies at its next start once they are a day old.
- **Microphone and system audio.** Clipmark records audio only while a screen recording runs and only if you turned on "Record the microphone" or "Record system audio" in Settings. Windows asks for your permission before Clipmark can use the microphone.
- **Text recognition and QR codes.** Copying text from a capture uses an OCR engine inside Clipmark (with the text recognition built into Windows as a fallback), and QR codes are read by a library inside Clipmark. All of them run on your PC. No image is uploaded.
- **Links.** Clipmark opens a web page only when you click a link: Buy Clipmark Premium, Source code and releases (GitHub), or Open link next to a QR code that contains a web address. Your browser then loads that page, and the website's own privacy policy applies.
- **Sharing.** When you click Share, Windows opens its own share window and you choose the target app. Clipmark does not send the file anywhere by itself.
- **Settings.** Your settings are stored in the app's local storage on your PC.
- **Capture history (optional, off by default).** If you turn it on, Clipmark keeps your last 25 captures, including your annotations, in the app's local storage on your PC. Each item is encrypted for your Windows account. The history never leaves your PC. Turning the history off deletes it.
- **License and trial.** Clipmark stores its license state in the app's local storage: the signed entitlement it received from the licence server (license number, plan, number of PCs, this installation's ID, issue and end dates), the installation ID, the key store kind and the times of activation and of the last server contact. The entitlement contains no name and no email address. The installation key (see "License activation and trial") is kept by Windows. Older versions stored a license key and a local trial record (also in the registry under HKCU\Software\Clipmark and in Windows Credential Manager, entry "Clipmark/Trial"); Clipmark still reads them until the first answer from the licence server and then deletes the registry and Credential Manager copies.

When you uninstall Clipmark, Windows deletes the app's local storage (settings, capture history and license state). Your screenshots and recordings stay where they were saved. The installation key stays in Windows so that a reinstall keeps its trial and seat (the README explains how to delete it), and so does an Ask AI key you did not remove (see below).

## License activation and trial

To start the trial, to activate a license and to keep it valid, Clipmark talks to IT-BAER's licence server at releash.it-baer.net over HTTPS.

- **The installation key.** On the first trial or activation call, Clipmark creates a key pair in Windows for your Windows account: in the PC's TPM security chip when there is one, otherwise in Windows' software key storage. The private half cannot be exported and never leaves the PC; Clipmark signs each call with it. The installation ID is a fingerprint (hash) of the public half.
- **What is sent.** The public half of the installation key, the key store kind ("tpm" or "software"), the app version and the platform ("windows"); at activation also the activation code or license key you entered. No device name, no user name, no captures. Every call is signed and carries a time stamp and a random value against replay.
- **When.** At the first start with an internet connection (trial start), when you activate, about every two weeks to renew the entitlement (at most once an hour while a call is due), and when you click "Remove from this PC". Without a connection Clipmark keeps working with the stored entitlement until it runs out.
- **What IT-BAER stores.** The installation ID, the public key, the key store kind, the app version, the platform, the times of the first and the last call, the end of the trial per installation key, the license number the installation is bound to and its status (active, removed, signed out, revoked). Requests are limited per network address; the address is used for that in memory and not stored with these records. Records are kept while the trial or license exists and then, where they belong to a purchase, for the accounting period required by Austrian law (generally 7 years).
- **Legal basis.** Performance of the license contract and of the trial you start (Art. 6(1)(b) GDPR), and IT-BAER's legitimate interest in preventing misuse of licenses and trials (Art. 6(1)(f) GDPR).

## Ask AI (optional)

Ask AI lets you ask Claude, Anthropic's AI model, questions about a capture. It works in two ways: with your own Anthropic API key saved in Settings, Clipmark sends directly to Anthropic and IT-BAER receives nothing; with Clipmark Premium activated on the PC and no own key, Clipmark sends through IT-BAER's relay (described below). An own key always takes precedence.

- **What is sent.** The capture as shown in the editor, including your annotations; redactions are burned in, so the hidden pixels are not sent. Large captures are scaled down to at most 2576 pixels on the long side. With the image go your question and the earlier questions and answers of the same conversation.
- **When.** Only when you send a question. The first time, Clipmark asks you to agree. "Test key" in Settings sends only a request that lists the available models, without any image.
- **Where.** Directly from your PC over HTTPS to Anthropic's API (api.anthropic.com), using your API key. Anthropic processes the data as your API provider under the terms of your Anthropic account (https://www.anthropic.com/legal/commercial-terms) and bills your account for each request. Anthropic's privacy policy: https://www.anthropic.com/legal/privacy.
- **What Clipmark keeps.** The conversation exists only in memory and ends when you start a new chat, open or take another capture, or close the capture. It is not written to disk and is not part of the capture history.
- **Through IT-BAER's relay (Premium without an own key).** The same image, question and earlier turns go over HTTPS to IT-BAER's relay (releash.it-baer.net), signed with the installation key, and the relay passes them to Anthropic under IT-BAER's API key; the first time, Clipmark asks you to agree to this path. The relay counts usage per installation (requests, tokens and cost) to enforce the Premium limits and keeps these counters; it does not store the image, your questions or the answers. Anthropic processes the request as IT-BAER's API provider (https://www.anthropic.com/legal/commercial-terms) and receives an identifier derived from the installation ID instead of the ID itself, for abuse detection.
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

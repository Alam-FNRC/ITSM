# FNRC Service Desk — Feature Spec

26 Oct 2026 · William (owner) · Claude (architect)

What we will add to the .NET service desk, taken from how iTop and GLPI work, described in our own words. Priorities are provisional until the ISO audit report and the current help desk screenshots arrive. Detailed reference notes: `reference/itop-model-notes.md`, `reference/glpi-feature-notes.md`. Known defects in the original code: `review/2026-10-code-review.md`.

**Keep it small (decided 1–2 Nov 2026).** FNRC's IT team is small. Each flow has only the steps and approvals that ISO 27001 or ITIL require. Where something needs approval, one person approves it: the IT manager, or a deputy given the same permission. There are no multi-step or percentage approvals and no CAB votes. Fields are optional unless an auditor needs them, dates are filled in automatically, and lists are imported from Excel rather than typed.

## Feature map

Priority: **P1** = needed for the next ISO audit, **P2** = needed for go-live, **P3** = later. Phases follow the roadmap in CLAUDE.md (1 security, 2 platform, 3 audit and reports, 4 ITIL modules, 5 pilot).

| Feature | Best reference | In our tool today | Priority | Phase |
| --- | --- | --- | --- | --- |
| Stored time measures (time to own, waiting, solve, close, effort) | GLPI | Done: own, waiting, solve, reopen and assignment counts (30 Oct); close (2 Nov). Effort left out: the team does not log time (decided 2 Nov) | P1 | 3 |
| KPI statistics by period and dimension | GLPI | Done 2 Nov: KPI reports page with CSV and print-to-PDF; the monthly evidence pack is still to do | P1 | 3 |
| Monthly PDF/CSV audit reports | Neither (we build) | CSV ticket export only (fixed 28 Oct) | P1 | 3 |
| Tamper-proof audit log incl. permissions and logins | Neither (we build) | Partial, with gaps | P1 | 3 |
| Priority from impact × urgency matrix | iTop, GLPI | No: priority set by hand | P1 | 4 |
| Ticket approvals (one approver per request type; decided 2 Nov) | GLPI | No | P2 | 4 |
| Pending reasons with auto-reminders and auto-resolve | GLPI | No | P2 | 4 |
| Solution accept / refuse, auto-close after N days | GLPI | Partial | P2 | 4 |
| Satisfaction surveys (sampled, scored) | GLPI | Basic survey exists | P3 | 4 |
| Ticket templates (mandatory, hidden, preset fields) | GLPI | Partial via custom fields | P2 | 4 |
| Parent / child tickets with cascade resolve | iTop | No | P2 | 4 |
| Major incidents (one outage, many reporters, status updates) | iTop | No | P2 | 4 |
| Security incidents: restricted access, triage, severity, response phases, evidence, lessons learned, reports from federal entities (proposed 28 Oct) | Neither (ISO 27001 A.5.24–A.5.28, A.6.8) | Stage 1 done 30 Oct: reporting, restricted access, triage, phases, action and access logs, affected items, members, review. Evidence done 1 Nov. Cut to four steps 2 Nov. Federal reports and moving tickets in are next | P1 | 4 |
| Business rules engine (routing, auto-assign) | GLPI | Partial: assignment rules | P2 | 4 |
| SLA matching by service and priority; OLAs; escalation levels | iTop, GLPI | Partial: SLA per priority/project | P2 | 4 |
| Risk register (replaces FNRC's Excel ISMS risk register; decided 30 Oct) | Neither (ISO 27001 6.1.2, 6.1.3, 8.2, 8.3) | Done 2 Nov: register, one approval, yearly review, reminders, Excel import. Links to incidents and assets later | P1 | 4 |
| IT KPI register (replaces the IT KPIs spreadsheet; decided 9 Nov) | Neither (ISO 27001 9.1, ISO 20000 9.1) | Built 9 Nov: capabilities and KPIs with owners, cycles and targets; owners enter values; year-end report; Excel import | P1 | 3 |
| Problem management | iTop, GLPI | No | P1 | 4 |
| Known errors (a problem status, not a separate record; 2 Nov) | iTop | No | P2 | 4 |
| Change management (standard, normal, emergency) with one approver, no CAB (decided 2 Nov) | iTop, GLPI | No | P1 | 4 |
| CMDB: configuration items and relationships | iTop | No | P2 | 4 |
| Impact analysis (affected services and contacts) | iTop | No | P3 | 4 |
| Assets with financial info and warranty | GLPI | No | P2 | 4 |
| Contracts, suppliers, software licences with expiry alerts | GLPI | No | P2 | 4 |
| Service catalogue (families, services, subcategories) | iTop | Done 2 Nov: services (with family and owner team) above the categories; SLA per service waits for SLA matching | P2 | 4 |
| Organisations, locations, teams | iTop | Departments and groups only | P3 | 4 |
| Email-to-ticket from a Microsoft 365 mailbox | GLPI (behaviour) | Unauthenticated JSON endpoint only | P2 | 2 |
| Recurrent tickets (e.g. monthly backup check) | GLPI | No | P3 | 4 |

## Tickets: incidents and requests

Keep the existing ticket, workflow and custom-field engine; add the following.

**Priority is calculated, never typed.** Agents set impact (department, service, person) and urgency (critical, high, medium, low); the system derives priority from a configurable matrix. Starting matrix:

| Impact | Urgency critical | High | Medium | Low |
| --- | --- | --- | --- | --- |
| Department | P1 | P1 | P2 | P4 |
| Service | P1 | P2 | P3 | P4 |
| Person | P2 | P3 | P3 | P4 |

**Time measures stored on every ticket** (working hours only, using the business calendar):

- Time to own: opening until the first update by an assigned agent or group member.
- Waiting time: total time in Pending.
- Time to solve and time to close: from opening, minus waiting time.
- Effort: sum of task durations. Not built: the team does not log time (decided 2 Nov 2026).

**Statuses.** New, Waiting for approval, Assigned, Pending, Solved, Closed, plus Rejected for requests. A per-role matrix controls who may move a ticket between statuses (extends today's `WorkflowTransitions`).

**Approvals for service requests.** A request type can need one approval, from a named user or from anyone holding the approver permission (for example the IT manager). The approver approves, or refuses with a reason, and a refused request is closed. Pending approvers get a reminder email. There are no multi-step or percentage approvals (decided 2 Nov 2026).

**Pending reasons.** Each reason (e.g. "Waiting for user") can post an automatic reminder every N days and auto-solve the ticket after N unanswered reminders.

**Solution and closure.** Posting a solution moves the ticket to Solved. The requester accepts (closes) or refuses with a reason (reopens). Unanswered solved tickets auto-close after a configurable number of days.

**Parent and child tickets.** Solving a parent incident solves all open children, copying the solution. Tickets can also be linked as duplicates or as related.

**Major incidents.** An agent can mark an incident as major when it stops a service for many users (e.g. email down). A major incident gets a named incident manager and the highest priority, and IT management is notified at once. Reports from other users about the same outage are linked as its children, and status updates posted on it go to every child's requester. Closing it needs a short note on the cause and can raise a Problem.

**Ticket templates per category.** Fields can be mandatory, preset, hidden or read-only; required fields can also be set per status (e.g. resolution code required to solve).

**Rules engine.** Ordered rules with AND/OR conditions on title, category, requester, requester's department, source, priority and email headers; actions set category, team, agent, priority, SLA or request an approval. Runs on create, update or both, with a log of which rules matched.

**Satisfaction survey.** Sent to a configurable percentage of closed tickets after N days; a comment is required for low scores.

## SLA, OLA and escalation

The existing SLA engine (business hours, pause on pending, pre-breach warning) stays and is extended.

- **Matching.** An SLA target is chosen by service, priority and ticket type (incident or request). If several match, the shortest wins; if none, no deadline.
- **OLAs.** Internal targets per support group (e.g. Network team must own within 30 minutes), tracked separately from the user-facing SLA.
- **Pending pushes the deadline.** Time in Pending moves the resolve deadline forward.
- **Thresholds.** Warning at 75% of the target, breach at 100%; a breach automatically moves the ticket to an Escalated state.
- **Escalation levels.** Each SLA can have levels that run at a set time before or after the deadline (e.g. 1 hour before: notify team lead; at breach: raise priority and notify IT manager).
- **Missing today.** Screens to maintain business hours and UAE public holidays.

## Security incidents

Proposed 28 Oct 2026; William to confirm. Neither iTop nor GLPI has a security incident module, so this section is built from ISO 27001:2022 A.5.24–A.5.28 (incident management) and A.6.8 (event reporting).

A security incident (phishing, malware, account compromise, data leak, lost laptop) is kept apart from ordinary incidents because its details are sensitive and auditors check how it was handled.

- **Restricted access.** Only the security team and the IT manager (new permission, e.g. `security.incident.view`) can see security incidents (confirmed 30 Oct). People they add to one incident, such as the server admin who must apply a fix, see that incident only. Security incidents never appear in general ticket lists, search, dashboards, Kanban, calendar, exports, reports or notifications for anyone else, including `report.view` holders. The reporter sees only that the report was received and its current status. Every time someone opens one, the system records who and when.
- **Reporting (A.6.8).** Any staff member can report a suspected security issue from a "Report a security issue" button with only a few required fields. Suspicious emails can later be forwarded to a dedicated mailbox (with email-to-ticket). An agent can move an ordinary ticket into the security area. From then on the restricted rules apply to it.
- **Triage (A.5.25).** The security team decides whether the report is not a security issue (it goes back to the service desk as a normal ticket), a security event (logged, no response needed) or a security incident. The decision, the reason and who made it are recorded.
- **Classification.** Category (phishing, malware, unauthorised access, account compromise, data leak or loss, lost or stolen device, denial of service, policy violation, other). Severity 1–4 is set from the impact on confidentiality, integrity and availability and the classification of the affected data. A flag marks incidents where personal data is involved.
- **Response steps (A.5.26).** New → Triage → Responding → Closed. Containment, eradication and recovery are work done while Responding and are written in the action log, not separate steps (cut from seven steps on 2 Nov 2026). Each step change is timestamped, and the times to triage and to close are stored for the KPIs. Severity sets the SLA targets and escalation, e.g. severity 1 notifies the security lead and IT manager at once.
- **Action log.** A dated, append-only record of every action taken (who, what, when), kept separate from comments. It cannot be edited or deleted.
- **Evidence (A.5.28).** Each evidence item (log export, screenshot, email, device) records a description, who collected it and when, and where it is kept. Files also get a SHA-256 hash. Every hand-over is recorded (chain of custody). Evidence files cannot be deleted or replaced. Built 1 Nov: files up to 25 MB (attachment types plus e-mails, Windows event logs and packet captures); items kept elsewhere, such as a laptop, record who holds them and where. The security team and people on the incident add evidence; only the security team records hand-overs. Every download and check re-computes the hash and is recorded; a file that no longer matches is refused and the security team is notified.
- **Affected items.** Affected users, devices, systems and services. These are linked to CMDB items once the CMDB exists.
- **Lessons learned (A.5.27).** Closing a severity 1 or 2 incident needs a short lessons-learned note; the other review fields (root cause, what worked, follow-up actions) are optional. Not needed for severity 3 and 4. Later, a follow-up action can raise a Problem or a Change.
- **KPIs.** Counts by category and severity, mean time to triage and to close, the share of incidents with lessons learned, and open incidents by age.

**Reports from federal entities** (confirmed 30 Oct). FNRC does not report incidents to outside bodies. Instead, federal entities send FNRC reports when their scans or monitoring find something that affects FNRC. The security team logs each report and works through the issues in it.

- **Report record.** Sending entity (from a list kept by an admin), date received, the sender's reference number, and the original document attached as evidence (hashed, cannot be deleted). Same restricted access as security incidents.
- **Entering the issues.** Formats differ by entity (PDF, spreadsheet, portal; confirmed 30 Oct), so the issues are typed in on one screen that adds several at a time. A spreadsheet import can come later for an entity whose layout stays the same.
- **One incident per issue.** Each issue in the report becomes a security incident linked to the report. When the same issue affects several servers (e.g. one missing patch on 10 servers), it is one incident with a line per affected server or address (confirmed 30 Oct). Each line is ticked off as fixed, or marked a false positive or accepted risk with a reason. The incident can be solved once every line is done. What the sender said (their severity, any reference such as a CVE number) is kept next to FNRC's own triage and severity.
- **Progress.** The report shows how many of its issues are open, in progress and closed. It is complete when every issue is closed.
- **Reply to the sender.** FNRC replies to the sending entity once the issues are handled, with no fixed deadline (confirmed 30 Oct). The report produces a status summary (PDF) listing each issue, its affected servers and what was done. The report also records when the reply was sent and to whom.
- **Repeat findings.** If a later report lists an issue already logged, the issue is linked to the earlier incident and reopens it if it was closed, so repeats are visible.
- **KPIs.** Reports per entity, time from receipt until all issues are closed, issues per report, and repeat findings.

## Risk register

Decided 30 Oct 2026: the service desk replaces FNRC's Excel register ("FNRC ISMS Risk Assessment register", v.12), which is never committed to the repository because it names vulnerable systems. Built from ISO 27001:2022 clauses 6.1.2 and 8.2 (assessment) and 6.1.3 and 8.3 (treatment).

- **Risk record.** Reference, risk scenario, threat, cause or vulnerability, existing controls (the Excel's "Risk Analysis & Assessment" columns).
- **Evaluation.** Impact on confidentiality, integrity and availability (1–5 each) and likelihood (1–5), with the "Assessment Criteria" descriptions shown as help. Score = average of the three impacts × likelihood, 1–25, as in the Excel (decided 30 Oct). Levels: Low up to 4, Medium up to 12, High above 12 (confirmed 2 Nov). The Excel's own bands leave gaps for fractional scores (a score of 12.67 shows "0"); these bands close them.
- **Treatment.** Option (Mitigate, Avoid, Accept, Transfer), recommended controls, ISO 27001 Annex A controls (picked from the 2022 list, not typed; FNRC has no separate Statement of Applicability), risk owner (a user), and an exact target date so reminders work.
- **Status.** Open → Treating → Done (decided 2 Nov). Overdue is shown automatically from the target date. The Excel's residual "Status" column is dropped.
- **One approval (6.1.3 f).** The IT manager approves the treatment plan, or the decision to accept the risk, with one click; who approved and when is recorded (decided 30 Oct). Changing the plan afterwards clears the approval.
- **Residual risk.** Impacts and likelihood after treatment, scored the same way, next to the original score.
- **History and reviews.** Every change to scores, treatment, owner and status goes to an append-only history. The register is reviewed once a year (decided 2 Nov): a reminder is sent, and marking a risk as reviewed records the date, reviewer and the scores at that time, so auditors can see how each risk changed.
- **Access.** The security team and the IT manager see every risk; a risk owner also sees the risks they own (decided 30 Oct). Same explicit-only permission rules as security incidents.
- **Links.** Security incidents (an incident can show that a risk happened, or reveal a new one), federal report findings, and later changes and assets (an optional link once the asset register exists; decided 2 Nov).
- **Reminders.** Owners are reminded before the target date; overdue treatments are listed.
- **Import.** The 14 risks in the Excel are imported, with its typos corrected ("Medum Risk", "Q432025", "A5.30" written as "A.5.30").
- **KPIs.** Risks by level and status, overdue treatments, original against residual level, accepted risks.

**Built 2 Nov 2026.** The register page shows counts that double as filters (high, medium and low not done, overdue, waiting for approval, due for review). The approver can approve all waiting risks at once, and the security team records the yearly review for the whole register in one step; each risk keeps the date, the reviewer and its scores of the day. Reminders go to the owner 14 days before the target date and when it passes (the security team too), and to the security team once a month while a review is due. The Excel import shows a preview with anything it could not read (such as the target date "Q432025"), asks which user each owner name in the file is, and skips rows already in the register. Not built yet: links to security incidents, federal findings and assets.

Points found in the Excel (30 Oct): scores and levels of risks 3–6 and 10–13 are typed rather than calculated; the treatment list says "Mitigate" where the criteria sheet says "Reduce"; the status drop-down sits on the Recommended Controls column, so Status is free text; the residual "Status" column is empty; risk 14 (IDOR) is mapped to A.5.30 (ICT readiness for business continuity) where A.8.28 or A.8.29 may fit better.

## IT KPI register

Decided 9 Nov 2026: the service desk replaces the IT KPIs spreadsheet (`docs/kpis-list.xlsx`, not committed), in which staff type the year's figures for 15 KPIs under 7 capabilities (digital strategy, digital transformation, digital channels, ISMS, resilience, partnerships, self-service portal). Evidence of monitoring and measurement for ISO 27001 9.1 and ISO 20000 9.1.

- **Capabilities and KPIs.** The sheet's columns: number, Arabic and English name, result type (number or percentage), direction (▲ higher is better, ▼ lower is better, ◆ monitored only), formula, data source, scope, description, unit. Set up by an administrator (`kpi.manage`, which SuperAdmin holds automatically, since the figures are not sensitive).
- **Owner and cycle (decided 9 Nov).** Each KPI has one owner (a user) and a measurement cycle of yearly, twice a year, quarterly or monthly, set by the administrator and never by the owner. The owner enters a value for each period, with an optional note (such as the list of systems counted), once the period has started; there is no approval step. Values can be corrected; the system audit log records who changed what.
- **Year-end figure.** Counts take the last value of the year, percentages and times the average; the administrator can change the rule per KPI.
- **Targets.** Optional, one per KPI per year; the report shows met or missed following the direction.
- **KPI 6 (average fault resolution time)** can be filled from this system's own tickets (working hours, as on the KPI reports page); the figure is shown as "from tickets" and the owner can store it or type another.
- **Access.** `kpi.manage` sees and edits everything and the year-end report; an owner sees and enters their own KPIs, and the menu shows the register to them.
- **Reminders.** When a period ends with no value, the owner gets one notification for that period.
- **Report.** The year-end table as in the sheet: KPI rows under their capability, one column per year, target and result, CSV download and printing to PDF.
- **Import.** The sheet's rows, with their 2023–2026 yearly values; rows whose number already exists are skipped. The sheet has no owners or targets: those are set afterwards. English names for the known KPIs are built in and can be edited.

Points found in the sheet (9 Nov): KPI 3 is marked a percentage but holds counts (11, 12, 14, 14); KPI 14 falls from 6 to 2 although higher is better; percentages are stored as fractions (0.99); KPI 5 holds text ("4 سنوات"). The import keeps the numbers and flags the text.

## Problem and Known Error

A **Problem** is the root cause behind one or more incidents. It reuses the ticket base (assignee, team, category, impact × urgency priority, tasks, time measures).

- **Statuses:** Open → Known error (cause and workaround written down) → Closed. Closed problems are read-only (kept short, 2 Nov).
- **Analysis fields:** Symptoms, Causes, Impact.
- **Links:** incidents and requests (many), the fixing Change, Known Errors.
- **Bulk solve:** solving a problem can, optionally, solve all linked open incidents with one action.

A **known error** is a problem whose cause and workaround are written down, not a separate record: symptom, root cause, workaround, permanent solution and affected items. Agents see matching known errors on new incidents; the AI copilot can use them as a source.

## Change management

Kept to what ITIL and ISO 27001 A.8.32 require (decided 2 Nov 2026): one approver, no CAB.

| Type | Approval | Example |
| --- | --- | --- |
| Standard (routine) | None: pre-approved | Password policy reset, monthly patching |
| Normal | One approval by the IT manager, or a deputy with the same permission | New firewall rule, server upgrade |
| Emergency | Done first, approved afterwards by the IT manager | Fix for a live outage |

**Flow:** New → Approved (or Rejected) → Done. Standard changes go straight from New to Done; Cancelled is possible from any open state.

**What each step asks for**

- To raise: what, why, planned date, and a rollback plan (one text box each).
- To approve: approve or reject, with an optional comment.
- To finish: the result (successful, failed, rolled back) and an optional note.

**No CAB.** The IT manager is the change authority and may consult others before approving.

**Links:** related incidents, problems and requests, and affected assets. **Permissions:** one to raise and carry out changes, one to approve them.

Audit value: change success rate, emergency change ratio and changes without approval are standard ISO 27001 A.8.32 evidence.

## CMDB, assets, contracts and suppliers

One model serves both: an **asset** is a physical or licensed item with financial data; a **configuration item (CI)** is anything whose failure affects a service. A server is both.

**Configuration items.** Start small and extend: Business service, Application, Server (physical or virtual), Network device, Database, PC/laptop, Printer, Phone. Common fields: name, owner department, business criticality (high, medium, low), status (in production, implementation, stock, retired), location, go-live date. Types are admin-defined with custom fields, reusing the existing EAV engine, so new types need no code.

**Relationships.** "Runs on", "depends on", "connected to", "part of". Shown as a simple dependency view. Later: impact analysis that walks the dependencies to list affected services and contacts on a ticket.

**Asset financial record.** Supplier, order and invoice numbers, purchase and delivery dates, value, warranty start and duration (expiry alert), decommission date, depreciation (linear, years). Status changes can auto-fill dates (e.g. moving to Retired stamps the decommission date).

**Contracts.** Number, type, supplier, start date, duration, notice period, renewal (none, automatic, explicit), cost lines, covered assets. Alerts at notice date and expiry.

**Suppliers.** Name, type, contacts, website, phone, email, contracts, supplied items; can be assigned to tickets.

**Software licences.** Software, licence type, quantity (or unlimited), expiry, assigned devices or users; flags over-use.

**Service catalogue.** Service family → service → subcategory; each subcategory says whether it raises an incident or a request, which template, which SLA. Feeds the self-service portal and SLA matching.

Built 2 Nov 2026, kept short for a small team: a service has a name, an optional family (a label that groups services on the ticket form), a description shown to staff, and an owner team. The existing categories are the subcategories: each belongs to a service and can name a default ticket type and a default team. The ticket form lists categories grouped by service, so one choice sets the service, the type and the team. A new ticket with no matching assignment rule goes to the category's team, or else to the service's owner team. The KPI reports filter and break down by service. The template per category already exists as the custom fields placed per category. The SLA per service waits for SLA matching (not picked yet).

**Integration later:** import from an inventory tool (e.g. Intune or SCCM) instead of manual entry; decide in phase 4.

## KPIs and reports for ISO audits

Every KPI filters by date range, department, category, priority, team and agent, and exports to PDF and Excel. A monthly evidence pack is generated automatically and archived.

Built 2 Nov 2026 (KPI reports page, `GET /api/reports/kpi`, report.view): opened, solved and closed; response and resolution SLA met; mean time to own, resolve and close; reopen and first-time-fix rates; satisfaction; opened against solved per month; open backlog by age; and a breakdown by team, agent, category, department and priority. CSV export, and PDF through the browser's print. The SLA figures cover the tickets opened in the period once their outcome is known; the times and counts follow the date of the event (owned, solved, closed). The change, problem, access and asset KPIs follow with those modules.

| KPI | Definition | Audit use |
| --- | --- | --- |
| SLA compliance % | Tickets met ÷ tickets due, split by time to own and time to solve | Service levels |
| Mean time to own | Average time to own, working hours | Responsiveness |
| Mean time to resolve (MTTR) | Average solve time minus waiting time, working hours | Performance |
| Opened vs solved per month | Counts by creation and solution date | Capacity, trend |
| Backlog ageing | Open tickets in 0–3, 4–7, 8–30, 30+ day buckets | Control |
| Reopen rate | Reopened ÷ solved | Quality |
| First-time fix rate | Solved without reassignment or reopen | Quality |
| Breached tickets by team | Late own / late solve / both | Accountability |
| Satisfaction average | Mean score, response rate | Customer view |
| Incidents by category and asset | Counts, top 10 | Problem detection |
| Change success rate | Successful ÷ closed changes | ISO 27001 A.8.32 |
| Emergency change ratio | Emergency ÷ all changes | Change control |
| Unauthorised changes | Implemented without approval (should be 0) | Change control |
| Problems open and age | Count, average age | Problem management |
| Access changes | Role/permission grants and revocations, by whom | ISO 27001 A.5.18 |
| User access review | Users, roles, last login, inactive accounts | ISO 27001 A.5.18 |
| Security incidents | Count by category and severity; time to triage, contain and resolve; share with a completed review | ISO 27001 A.5.24–A.5.28 |
| Federal entity reports | Reports received, issues still open, time until all issues are closed, repeat findings | ISO 27001 A.5.26, A.8.8 |
| Asset register, warranty and contract expiries | Current list, upcoming 90 days | ISO 27001 A.5.9 |

ISO 27001 control numbers refer to the 2022 edition and are a guide; the final list follows what FNRC's auditors asked for.

## Email-to-ticket via Microsoft 365

Replace today's unauthenticated ingest endpoint with a background service that reads the help desk mailbox through Microsoft Graph (app registration with mailbox-scoped permission only).

- **New mail → new ticket.** Requester from the sender (matched to the AD user); CC recipients become watchers; source = email; attachments imported up to a size limit; ticket rules and SLA apply as normal.
- **Replies → comments.** Matched first by message headers, then by a subject tag such as `[FNRC-SD #1234]`. A reply to a solved ticket can reopen it. Quoted earlier text is stripped.
- **Loop protection.** Ignore our own notifications, auto-replies (by headers) and a configurable sender blacklist; strip standard disclaimers.
- **Unknown senders.** Reject with a polite reply, or create as an external contact; never auto-create login accounts.
- **Housekeeping.** Move processed mail to Processed or Rejected folders; log every outcome.

Outgoing notifications also move from SMTP to Graph, with a persistent queue so nothing is lost on restart.

## Rules for using iTop and GLPI

iTop is AGPL and GLPI is GPL. This spec and the reference notes were written by reading their data models and describing the concepts in plain English. Build from these documents, never from their source code; no code, SQL or templates are copied or translated. Reference copies used: iTop default branch and GLPI commit `753fc36`, both read 26 Oct 2026.

## Open questions

- [ ] Which reports and evidence did the ISO 27001 auditors ask for last year? (Sets the final P1 list.)
- [ ] Which IT governance standard is being pursued: ISO/IEC 20000-1, ISO/IEC 38500 or another?
- [ ] Fields, categories and statuses of the current help desk, incident and change screens (screenshots).
- [ ] Does FNRC already have an asset inventory tool (Intune, SCCM, spreadsheet)?
- [x] Who sits on the CAB? No CAB: the IT manager approves changes alone (2 Nov).
- [ ] Does the service desk serve only internal staff, or also other entities?
- [x] Who may see security incidents? The security team and the IT manager, plus people added to a single incident (30 Oct).
- [x] Must FNRC report security incidents to an outside authority? No. It receives reports from federal entities and logs their issues as incidents (30 Oct).
- [x] How do federal reports arrive? It varies by entity, so issues are entered by hand at first (30 Oct).
- [x] Does FNRC reply to the sending entity? Yes, with a status summary; no fixed deadline (30 Oct).
- [x] One incident per issue or per server? Per issue, with the affected servers listed inside it (30 Oct).
- [ ] Does FNRC already have an incident response procedure or severity scale to follow?
- [x] Risk register (2 Nov): optional link to assets later; status Open → Treating → Done, overdue shown automatically; the residual "Status" column is dropped; exact target dates; reviewed yearly; Annex A controls picked from the 2022 list (no separate SoA); Low up to 4, Medium up to 12, High above 12.

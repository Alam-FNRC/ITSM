# FNRC Service Desk — Feature Spec

26 Oct 2026 · William (owner) · Claude (architect)

What we will add to the .NET service desk, taken from how iTop and GLPI work, described in our own words. Priorities are provisional until the ISO audit report and the current help desk screenshots arrive. Detailed reference notes: `reference/itop-model-notes.md`, `reference/glpi-feature-notes.md`. Known defects in the original code: `review/2026-10-code-review.md`.

## Feature map

Priority: **P1** = needed for the next ISO audit, **P2** = needed for go-live, **P3** = later. Phases follow the roadmap in CLAUDE.md (1 security, 2 platform, 3 audit and reports, 4 ITIL modules, 5 pilot).

| Feature | Best reference | In our tool today | Priority | Phase |
| --- | --- | --- | --- | --- |
| Stored time measures (time to own, waiting, solve, close, effort) | GLPI | Done 30 Oct for own, waiting and solve, plus reopen and assignment counts; close and effort wait for solution acceptance and tasks | P1 | 3 |
| KPI statistics by period and dimension | GLPI | No | P1 | 3 |
| Monthly PDF/CSV audit reports | Neither (we build) | CSV ticket export only (fixed 28 Oct) | P1 | 3 |
| Tamper-proof audit log incl. permissions and logins | Neither (we build) | Partial, with gaps | P1 | 3 |
| Priority from impact × urgency matrix | iTop, GLPI | No: priority set by hand | P1 | 4 |
| Ticket approvals with steps and thresholds | GLPI | No | P2 | 4 |
| Pending reasons with auto-reminders and auto-resolve | GLPI | No | P2 | 4 |
| Solution accept / refuse, auto-close after N days | GLPI | Partial | P2 | 4 |
| Satisfaction surveys (sampled, scored) | GLPI | Basic survey exists | P3 | 4 |
| Ticket templates (mandatory, hidden, preset fields) | GLPI | Partial via custom fields | P2 | 4 |
| Parent / child tickets with cascade resolve | iTop | No | P2 | 4 |
| Major incidents (one outage, many reporters, status updates) | iTop | No | P2 | 4 |
| Security incidents: restricted access, triage, severity, response phases, evidence, lessons learned, reports from federal entities (proposed 28 Oct) | Neither (ISO 27001 A.5.24–A.5.28, A.6.8) | Stage 1 done 30 Oct: reporting, restricted access, triage, phases, action and access logs, affected items, members, review. Evidence, federal reports and moving tickets in are next | P1 | 4 |
| Business rules engine (routing, auto-assign) | GLPI | Partial: assignment rules | P2 | 4 |
| SLA matching by service and priority; OLAs; escalation levels | iTop, GLPI | Partial: SLA per priority/project | P2 | 4 |
| Risk register (replaces FNRC's Excel ISMS risk register; decided 30 Oct) | Neither (ISO 27001 6.1.2, 6.1.3, 8.2, 8.3) | No: kept in Excel | P1 | 4 |
| Problem management | iTop, GLPI | No | P1 | 4 |
| Known Error records | iTop | No | P2 | 4 |
| Change management (standard, normal, emergency) with CAB approval | iTop, GLPI | No | P1 | 4 |
| CMDB: configuration items and relationships | iTop | No | P2 | 4 |
| Impact analysis (affected services and contacts) | iTop | No | P3 | 4 |
| Assets with financial info and warranty | GLPI | No | P2 | 4 |
| Contracts, suppliers, software licences with expiry alerts | GLPI | No | P2 | 4 |
| Service catalogue (families, services, subcategories) | iTop | No: only categories | P2 | 4 |
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
- Effort: sum of task durations.

**Statuses.** New, Waiting for approval, Assigned, Pending, Solved, Closed, plus Rejected for requests. A per-role matrix controls who may move a ticket between statuses (extends today's `WorkflowTransitions`).

**Approvals for service requests.** An approval has one or more named steps (e.g. Line manager, IT Security). Each step goes to a user or a group and has a minimum approval percentage; 0% means the first answer decides. Any refused step refuses the request. Pending approvers get reminder emails.

**Pending reasons.** Each reason (e.g. "Waiting for user") can post an automatic reminder every N days and auto-solve the ticket after N unanswered reminders.

**Solution and closure.** Posting a solution moves the ticket to Solved. The requester accepts (closes) or refuses with a reason (reopens). Unanswered solved tickets auto-close after a configurable number of days.

**Parent and child tickets.** Solving a parent incident solves all open children, copying the solution. Tickets can also be linked as duplicates or as related.

**Major incidents.** An agent can mark an incident as major when it stops a service for many users (e.g. email down). A major incident gets a named incident manager and the highest priority, and IT management is notified at once. Reports from other users about the same outage are linked as its children, and status updates posted on it go to every child's requester. Closing it requires a short review and usually raises a Problem.

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
- **Response phases (A.5.26).** New → Triage → Containment → Eradication → Recovery → Post-incident review → Closed. Each phase change is timestamped, and the times to triage, contain and resolve are stored for the KPIs. Severity sets the SLA targets and escalation, e.g. severity 1 notifies the security lead and IT manager at once.
- **Action log.** A dated, append-only record of every action taken (who, what, when), kept separate from comments. It cannot be edited or deleted.
- **Evidence (A.5.28).** Each evidence item (log export, screenshot, email, device) records a description, who collected it and when, and where it is kept. Files also get a SHA-256 hash. Every hand-over is recorded (chain of custody). Evidence files cannot be deleted or replaced.
- **Affected items.** Affected users, devices, systems and services. These are linked to CMDB items once the CMDB exists.
- **Lessons learned (A.5.27).** Closing a severity 1 or 2 incident requires a post-incident review: root cause, what worked, what to improve, and follow-up actions. Each follow-up action can raise a Problem or a Change. The review is optional for severity 3 and 4.
- **KPIs.** Counts by category and severity, mean time to triage, contain and resolve, the share of incidents with a completed review, and open incidents by age.

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
- **Evaluation.** Impact on confidentiality, integrity and availability (1–5 each) and likelihood (1–5), with the "Assessment Criteria" descriptions shown as help. Score = average of the three impacts × likelihood, 1–25, as in the Excel (decided 30 Oct). Levels: Low up to 4, Medium up to 12, High above 12. The Excel's own bands leave gaps for fractional scores (a score of 12.67 shows "0"); these bands close them (to confirm).
- **Treatment.** Option (Mitigate, Avoid, Accept, Transfer), recommended controls, ISO 27001 Annex A controls (picked from the 2022 list, not typed), risk owner (a user), target date, status. The IT manager approves each treatment plan and every decision to accept a risk; who approved and when is recorded (decided 30 Oct).
- **Residual risk.** Impacts and likelihood after treatment, scored the same way, next to the original score.
- **History and reviews.** Every change to scores, treatment, owner and status goes to an append-only history. A periodic review records its date, reviewer and the scores at that time, so auditors can see how each risk changed.
- **Access.** The security team and the IT manager see every risk; a risk owner also sees the risks they own (decided 30 Oct). Same explicit-only permission rules as security incidents.
- **Links.** Security incidents (an incident can show that a risk happened, or reveal a new one), federal report findings, and later changes and CMDB assets.
- **Reminders.** Owners are reminded before the target date; overdue treatments are listed.
- **Import.** The 14 risks in the Excel are imported, with its typos corrected ("Medum Risk", "Q432025", "A5.30" written as "A.5.30").
- **KPIs.** Risks by level and status, overdue treatments, original against residual level, accepted risks.

Points found in the Excel (30 Oct): scores and levels of risks 3–6 and 10–13 are typed rather than calculated; the treatment list says "Mitigate" where the criteria sheet says "Reduce"; the status drop-down sits on the Recommended Controls column, so Status is free text; the residual "Status" column is empty; risk 14 (IDOR) is mapped to A.5.30 (ICT readiness for business continuity) where A.8.28 or A.8.29 may fit better.

## Problem and Known Error

A **Problem** is the root cause behind one or more incidents. It reuses the ticket base (assignee, team, category, impact × urgency priority, tasks, time measures).

- **Statuses:** New, Assigned, Pending, Solved, Under observation, Closed. Closed problems are read-only.
- **Analysis fields:** Symptoms, Causes, Impact.
- **Links:** incidents and requests (many), the fixing Change, Known Errors.
- **Bulk solve:** solving a problem can, optionally, solve all linked open incidents with one action.
- **Roles:** Problem Manager assigns and closes.

A **Known Error** is a knowledge record, not a ticket: symptom (required), root cause, workaround, permanent solution, error code, domain (application, network, server, desktop), affected configuration items, originating problem. Agents see matching known errors on new incidents; the AI copilot can use them as a source.

## Change management and CAB

| Type | Validation (acceptance) | CAB approval | Example |
| --- | --- | --- | --- |
| Standard (routine) | No | No, pre-approved | Password policy reset, monthly patching |
| Normal | Yes | Yes | New firewall rule, server upgrade |
| Emergency | No | Yes, by emergency approvers | Fix for a live outage |

**Normal change flow:** New → Validated (or Rejected) → Assigned → Planned → Approved (or Not approved → back to Planned) → Implemented → Under review → Closed. Cancelled is possible from any open state. Standard changes skip validation and approval; emergency changes skip validation only.

**Required per step**

- To validate: supervisor group, manager group, acceptance comment.
- To plan: start and end dates, impact description, outage yes/no, implementation plan, **rollback plan**, test plan, checklist.
- To approve: CAB decision and comment.
- To close: outcome (successful, partially successful, failed, rolled back) and post-implementation review notes.

**CAB.** Neither iTop nor GLPI has a real CAB object, so we build one: a CAB group, approval steps with a minimum percentage (reusing ticket approvals), and a "waiting for CAB" list for the meeting. Meeting minutes are optional, later.

**Links:** related incidents, problems, requests, affected configuration items, child changes. **Roles:** Change Supervisor (validate, assign, close), Change Implementer (plan, implement), Change Approver (CAB).

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

**Integration later:** import from an inventory tool (e.g. Intune or SCCM) instead of manual entry; decide in phase 4.

## KPIs and reports for ISO audits

Every KPI filters by date range, department, category, priority, team and agent, and exports to PDF and Excel. A monthly evidence pack is generated automatically and archived.

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
- [ ] Who sits on the CAB, and how often does it meet?
- [ ] Does the service desk serve only internal staff, or also other entities?
- [x] Who may see security incidents? The security team and the IT manager, plus people added to a single incident (30 Oct).
- [x] Must FNRC report security incidents to an outside authority? No. It receives reports from federal entities and logs their issues as incidents (30 Oct).
- [x] How do federal reports arrive? It varies by entity, so issues are entered by hand at first (30 Oct).
- [x] Does FNRC reply to the sending entity? Yes, with a status summary; no fixed deadline (30 Oct).
- [x] One incident per issue or per server? Per issue, with the affected servers listed inside it (30 Oct).
- [ ] Does FNRC already have an incident response procedure or severity scale to follow?
- [ ] Risk register: should risks link to an asset list? The impact criteria are per asset, but the Excel has no asset column.
- [ ] Risk register: statuses beyond Planned and Closed (e.g. Open, In progress, Accepted, Overdue)? What is the residual "Status" column for?
- [ ] Risk register: target dates as quarters or exact dates (exact dates allow reminders)? How often is the register reviewed?
- [ ] Is there a Statement of Applicability to pick Annex A controls from?
- [ ] Risk levels for fractional scores: is Low up to 4, Medium up to 12, High above 12 right?

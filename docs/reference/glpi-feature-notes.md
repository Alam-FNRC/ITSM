# GLPI reference spec (clean-room): stats, assets, ITIL features and mail collector

Clean-room notes on GLPI (GPL), commit `753fc36`, read on 26 Oct 2026. Concepts only, in our own words; no GLPI code is copied. Use with ../feature-spec.md.

---

## 1. Statistics, KPIs and time measures

### 1.1 Stored time measures on every ticket, problem and change
GLPI stores these durations on the record when it changes state, so reports don't recompute them. All are in seconds.

| Measure | Definition |
|---|---|
| **Take-into-account delay** (Time to own, actual) | Time from opening until a technician first "owns" the ticket. Uses the time-to-own SLA calendar, so only working hours count; with no calendar it is plain elapsed time. The minimum is 1 second. The moment of ownership is also stored as a "take-into-account date". |
| **Waiting duration** | Total time spent in Pending. A "begin waiting date" is set when the record enters Pending. On leaving Pending, the elapsed time (working hours if there is a calendar) is added to the total and the start date is cleared. |
| **SLA waiting duration** | A separate pending total used to push the time-to-resolve deadline forward. |
| **Solve delay** | Time from opening to the solution date, minus waiting duration, never below 0. Uses the entity calendar when one is set. |
| **Close delay** | Time from opening to the closure date, minus waiting duration. Same calendar rule. |
| **Action time** | Sum of the durations of all tasks: the real effort spent. |

**What counts as "taking into account".** A ticket is taken into account the first time an assigned user, or a member of an assigned group, updates it. An update by a requester who is not also assigned does not count. A business rule can set a "do not compute take-into-account delay" flag, which skips this measure.

### 1.2 Statistics module
Statistics exist for tickets, problems and changes, with four views each:
- **Global**: monthly trends.
- **By object**: one row per value of a chosen dimension.
- **By hardware characteristics**: type, model, OS, location of the linked asset.
- **By hardware**: the individual linked asset.

**KPIs per period and per dimension value:**
- **Opened:** count by creation date.
- **Solved:** count by solution date, for records that are solved or closed.
- **Late:** solved after their time-to-resolve deadline.
- **Closed:** count by closure date.
- **Solved with action time:** solved records that have more than zero task time.
- **Average time to take into account:** average take-into-account delay of solved records.
- **Average time to resolution:** average solve delay.
- **Average time to closure:** average close delay.
- **Average and total real duration of treatment:** average and sum of action time. When the dimension is "technician in tasks", it uses task-level time instead.
- **Satisfaction surveys opened:** closed records that have a survey.
- **Satisfaction surveys answered:** surveys with a response date.
- **Average satisfaction:** mean score of answered surveys.

**Dimensions you can break KPIs down by:**
- **Requester side:** requester user, writer (who created it), requester group (flat or as a tree), user title, user category.
- **Characteristics:** category (flat or tree), urgency, impact, priority, solution type. For tickets only, also type (incident or request), request source, and location (flat or tree).
- **Assigned side:** technician as assigned, technician who did tasks, assigned group (flat or tree), assigned supplier.

Results can be exported to CSV or saved as an image.

### 1.3 Dashboards
GLPI has configurable grid dashboards made of cards. Every card respects a shared filter bar with these filters:
- dates (creation or last modification)
- entity
- category
- location
- manufacturer
- request source
- SLA
- asset state
- ticket status
- ticket type
- requester group
- technician group
- technician user

**Ticket count cards.** Each is available as a big number and as a list:
- Late tickets: time-to-own or time-to-resolve exceeded, for either the SLA or the OLA.
- Tickets waiting for *my* approval.
- Not-solved tickets.
- New, Pending, Assigned, Planned, Solved and Closed tickets.

**Chart cards:**
- Tickets by month.
- Ticket evolution over the past year: new, assigned, pending, to approve, solved, closed.
- Ticket status by month.
- Tickets by category and entity.
- Opened vs solved by group.
- Tickets by status.
- **Ticket times in hours, per month**: average time to own, waiting time, time to resolve and time to close.
- **Tickets by SLA status and technician**, and **by technician group**. The SLA buckets are: On time, Late own, Late resolve, Late own and resolve.
- Tickets summary.
- Top-N rankings by category, entity, request source, location, requester, requester group, observer, observer group, assignee and assignee group.

**Asset cards:**
- Count per asset type, and count per type of each asset.
- Computers by operating system.
- Computers by age: under 1 year, 1–3 years, 3–5 years, over 5 years.

**Other cards:** reminders list and an editable markdown card. There is also a demo-data mode.

### 1.4 Satisfaction surveys (tickets and changes)
- **Survey record:** type (internal or external), creation date, response date, score, comment, end date.
- **Settings per entity**, with separate sets for tickets and changes, each inherited from the parent entity unless overridden:
  - Survey type.
  - **Rate**: the percentage of closed records that receive a survey, chosen at random.
  - **Delay**: days after closure before the survey is created.
  - **Duration**: days the survey stays answerable; 0 means it never expires.
  - Maximum score and default score.
  - **Mandatory-comment threshold**: a comment is required if the score is at or below this value.
  - External survey URL template, with placeholders for the record ID, category and similar.
- **Rules:**
  - A scheduled job looks at records closed since the last processed closure date, after the delay, and creates surveys at random according to the rate.
  - An answer can still be edited for 12 hours after it is submitted.

---

## 2. Asset and inventory management

### 2.1 Asset types
- **Native types:** computer, monitor, network equipment, peripheral, phone, printer, software licence, certificate, appliance, and "unmanaged" (discovered but not yet classified).
- **Financial information** can also be attached to: cartridges and consumables, software, telecom lines, domains, racks, enclosures, power distribution units, passive data-centre equipment, database instances, cables and SIM cards.

### 2.2 Custom asset definitions
Administrators can define new asset types without code. Each type is given a set of opt-in **capabilities**:
- financial info, contracts, documents, history, notes
- software, operating system, devices and components, volumes, network ports, antivirus
- virtual machines, database instances, appliances, certificates, domains
- impact analysis, knowledge base links, remote management, peripherals, sockets and plugs
- can be put in a rack, can be reserved, can be inventoried, can be a project asset, shows in global search

Custom fields can be boolean, date, date-time, dropdown, number, string, text or URL. Each custom type also gets its own model and type lists.

### 2.3 Lifecycle and state
- **States** are a configurable list. Each state has a per-asset-type "visible for" flag, so for example "In repair" can apply only to computers.
- Assets can be soft-deleted (sent to a trash).
- **Automatic date filling** is configured per entity. A change to a given state (for example "In stock" or "Retired") can automatically set any of these dates: order, purchase, delivery, startup, warranty start, decommission. Each can also be copied from another date (warranty from purchase, for example) or set at automatic inventory import.
- **Dictionary rules** normalise manufacturer, model, type, operating system and software names that arrive from inventory.

### 2.4 Financial and administrative information (one record per asset)

| Field | Meaning |
|---|---|
| Supplier, budget | Links to those records |
| Order number, delivery form number, invoice number, immobilisation (fixed-asset) number | Reference numbers |
| Order date, purchase date, delivery date, startup date, last physical inventory date, decommission date | Lifecycle dates |
| Value | Purchase value |
| Warranty start date, warranty duration (months), warranty info, warranty extension value | Warranty |
| Warranty expiration | Derived from start date plus duration |
| Amortisation type (linear or decreasing), amortisation duration (years), amortisation coefficient | Depreciation. The value per year is derived from the startup or purchase date and a configurable fiscal year start. |
| Business criticality | Hierarchical list |
| Email alarms | Warranty-expiry alerts |
| Comments | Free text |

**Total cost of ownership (TCO)** is the sum of the costs of tickets linked to the asset plus the asset's value. The monthly TCO divides that total by the months since purchase. The ticket-cost part is stored on the asset and refreshed whenever ticket costs change.

### 2.5 Contracts
- **Fields:**
  - name, number, type, status, location, account number
  - start date, duration in months, **notice period** in months, **periodicity** (billing or renewal interval), **invoice period**
  - **renewal mode**: never, tacit (automatic) or express (must be renewed explicitly)
  - maximum number of covered items
  - support hours per weekday, comments, email alarms
- **Derived values:** expiration date, and "expiration minus notice".
- **Costs:** multiple cost lines, each with name, start and end dates, amount and budget, plus a total cost.
- **Links:** covered items, suppliers, users.
- **Alerts:** at end of contract, at the notice date, and at each periodicity date. They are sent per entity, a configurable number of days before the event.

### 2.6 Suppliers
Supplier fields:
- name, type, active flag
- address, postal code, city, state, country
- website, phone, fax, email
- administrative number

A supplier links to contacts, contracts and the items it supplied. Suppliers can be assigned to tickets. An entity option can make followups from suppliers private.

### 2.7 Software licences
- **Fields:** software, name, serial number, inventory number, licence type, number of licences (or unlimited), "allow over-quota" flag, purchase version, version in use, expiration date, status, technician and group in charge, user and group.
- **Derived values:**
  - **Valid**: installs do not exceed the quota, unless over-quota is allowed.
  - Number of installations and number of assigned items.
- Expiry alerts are sent per entity.

### 2.8 Budgets
Fields: name, type, start date, end date, value. A budget report shows spending against the budget from financial information, contract costs and ticket costs.

---

## 3. Ticket features

### 3.1 Core model
**Ticket statuses:** New, Approval, Processing (assigned), Processing (planned), Pending, Solved, Closed.
- Status moves automatically: to Assigned when a technician is set, and to Planned when a task is scheduled.
- A per-profile **status matrix** controls which status changes each role may make.

**Types:** Incident or Request.

**Priority** comes from a configurable urgency × impact matrix, with a fallback to their rounded average. Urgency and impact levels can be hidden through masks.

**Actors** can be requesters, assigned or observers. Each can be a user, group or supplier, and each has a "receive notifications" flag and an alternative email.

**Links between records:** linked to, duplicates / duplicated by, parent of / child of.

**Other fields:** request source, location, linked items. Tickets can be linked to problems, changes and projects.

**Auto-assignment** is configured per entity. Either the technician and group come from the linked hardware first and the category second, or the other way round.

### 3.2 Tasks
- **Fields:** content, category, state (Information, To do or Done), duration, planned start and end, assigned technician and/or group, private flag, task template, and a timeline position.
- Task durations add up to the ticket's action time.

### 3.3 Followups and pending reasons
- Followups can be private and can come from a followup template.
- **Pending reasons** define:
  - a calendar
  - an **automatic followup frequency** plus a followup template: the system posts reminder followups while the ticket is pending
  - a **number of followups before automatic resolution** plus a solution template: after that many reminders with no reply, the ticket is solved automatically
  - "default pending reason" and "pending by default" flags

### 3.4 Solutions and closure
- **Solution fields:** content, solution type, solution template, and a status (waiting for approval, accepted or refused).
- **What happens after a solution is posted:**
  - The ticket becomes Solved.
  - The requester can **accept** the solution, which closes the ticket and records who accepted it and when.
  - The requester can **refuse** it, which reopens the ticket and requires a reason.
  - A followup on a solved ticket can **reopen** it (a reason is required). Reopening is allowed from Solved, Closed, Pending and Observed.
- **Automatic closure:** the entity's autoclose delay closes solved tickets after N days. A delay of 0 closes immediately; "never" is also an option. A separate autopurge delay deletes closed tickets.
- From a linked problem or change, a bulk "solve tickets" action solves all its linked tickets.

### 3.5 Approvals (validations)
- **Approval request fields:**
  - approver target (a user, or a group, where any member can answer)
  - requester
  - submission comment and date
  - answer comment and date
  - status: None, Waiting, Accepted or Refused
- **Approval steps:** each request belongs to a named step, such as "Manager" or "Finance". Each step has a **minimum percentage of approvers required** (0–100). How a step's status is worked out:
  - If enough approvers have accepted to reach the threshold, the step is Accepted.
  - If the threshold can still be reached once the remaining answers arrive, the step is Waiting.
  - Otherwise the step is Refused.
  - When the threshold is 0%, the first answer decides the step.
- **Overall approval status** of a ticket:
  - None if there are no approval requests.
  - Refused if any step is refused.
  - Otherwise Waiting if any step is waiting.
  - Otherwise Accepted.
- **Approval templates:** preset approver targets plus a step.
- **Reminders:** a scheduled job re-notifies pending approvers. The entity sets the repeat interval, and the last reminder date is tracked on each request.
- A business rule can ask the manager of the requester's group or the technician's group to approve, and can set the step and threshold.

### 3.6 Ticket templates (also for changes and problems)
- Each template sets fields as **mandatory**, **predefined** (default value), **hidden** or **read-only**.
- Tickets can additionally template request source, the time-to-own and time-to-resolve SLAs and OLAs, and their deadlines.
- **How a template is chosen:**
  - A category points to one template for incidents and one for requests. Changes and problems each have their own template.
  - An entity sets the default template.
  - Category flags decide which object types the category is visible for (incident, request, problem, change) and whether it shows in the self-service portal.

### 3.7 Recurrent tickets and changes
- **Fields:**
  - name, active flag, template
  - start date, end date
  - **periodicity**: a time interval, or months and years
  - **create before**: how far ahead of the due time the record is created
  - calendar (used to skip non-working days)
  - next creation date (computed)
- Recurrent tickets can be linked to assets, with an option for **one ticket per item**.

### 3.8 Business rules engine
Rules exist for:
- tickets, changes and problems
- assets
- mail-collector entity routing
- user rights and authorisation
- locations
- software categories
- inventory import, plus the dictionaries above

**Rule structure:**
- Ordered rules, each with a name, active flag, entity and description.
- Criteria are combined with **AND or OR**.
- For ITIL rules, a trigger: **on add, on update, or both**.
- Each collection can either stop at the first matching rule or let all rules run. Any rule can also stop the processing that follows it.
- A log records which rules matched.

**Condition operators:** is, is not, contains, does not contain, starts with, ends with, regex matches, regex does not match, exists, does not exist, is empty, under / not under (for trees), IP is in range / not in range (CIDR), and date before / after / equal / not equal.

**Ticket rule criteria:**
- title, description, opening date, last update
- category and category code
- writer, requester, requester group, requester location
- technician, technician group, assigned supplier, observers and observer groups
- request source, item type, item group, item state, item location
- entity, default profile
- urgency, impact, priority, status, type
- contract type, approval status, approval step
- mail collector and email headers (From, To, Subject, Reply-To, In-Reply-To, X-Priority)
- location and location code, project, SLAs and OLAs

**Actions:** assign a value, add a value, assign or add from a regex capture, recalculate, do not calculate, copy from user (or user default), copy from item.

**Action targets:**
- category, or category from its code or full name
- requester, observer, technician and group (including from the requester's group or the item's group)
- supplier, urgency, impact, priority, status, type, location, request source, contract
- time-to-own and time-to-resolve SLAs and OLAs
- associated items
- approval requests (to a user, a group, or a group manager), approval step and threshold
- "do not compute take-into-account delay"
- apply a solution, task or followup template

### 3.9 SLAs, OLAs and escalation
- **Service level (SLM) container:** owns a calendar or uses the ticket's calendar; "no calendar" means 24/7.
- **Each SLA or OLA:**
  - kind: **time to own** or **time to resolve**
  - maximum time, as a number plus a unit (minute, hour, day or month)
  - **end of working day** flag: day-based deadlines snap to the end of the working day
- **Deadlines:**
  - Computed through the calendar, which supports working segments and holidays.
  - Time spent pending pushes the resolve deadline forward.
  - OLAs set separate *internal* time-to-own and time-to-resolve deadlines, and OLAs can be assigned to groups.
- **Escalation levels (per SLA or OLA):**
  - name, active flag, **execution time** relative to the deadline (minus N minutes/hours/days, at the deadline, or plus N), and AND/OR logic
  - their own criteria (for example status) and actions, which use the ticket rule actions: reassign, raise priority, notify, and so on
  - pending levels are queued per ticket and run by a scheduled job; they are replayed if the deadline moves

---

## 4. Change and Problem modules

### 4.1 Problem
- **Statuses:** New, Accepted, Processing (assigned), Processing (planned), Pending, Solved, Under observation, Closed. A closed problem cannot be edited.
- **Analysis fields** (rich text): **Impacts**, **Causes**, **Symptoms**.
- **Shares with tickets:** actors, category, urgency/impact/priority, dates, the four time measures, tasks, costs, solution, linked items, and templates.
- **Links:** linked tickets (with bulk solve), linked changes, other problems, suppliers.
- Problem statistics have the same views as tickets but no satisfaction surveys.
- Auto-assignment uses the category only.
- Problems have their own business rule set.

### 4.2 Change
- **Statuses:** New, Evaluation, Approval, Accepted, Pending, Testing, Qualification, Applied (the "solved" equivalent), Review, Closed, Cancelled, Refused.
- **Analysis tab:**
  - **Impacts** (impact analysis)
  - **Control list**
- **Plans tab:**
  - **Deployment plan**
  - **Backup / rollback plan**
  - **Checklist**
- **Approvals:** the same step-and-threshold model as tickets, plus a "can validate changes" right.
- **Change-specific features:** a satisfaction survey with its own entity settings, recurrent changes, and a change rule set.
- **Links:** tickets (with bulk solve), problems, other changes, items, suppliers, projects.
- **Costs:** each cost line has name, start and end dates, duration, **time cost** (an hourly rate), **fixed cost**, **material cost** and budget. The total is duration × time cost + fixed cost + material cost. The same cost model applies to tickets and problems.

---

## 5. Mail collector (email to ticket)

### 5.1 Configuration fields
- name, active flag, entity
- server type (IMAP or POP), host, port, TLS/SSL, certificate validation, mailbox folder
- login and password
- **accepted archive folder** and **refused archive folder** (both optional)
- **maximum attachment size** (0 means attachments are not imported)
- **use mail date** instead of collection date
- **requester field**: From or Reply-To
- **add CC recipients as observers**
- **collect only unread**
- **create user from email**
- connection error count and last collection date
- comments

### 5.2 Behaviour
- **Collection run:**
  - Collects up to a configured number of messages per run. It stops early if many messages are already seen or failed, to avoid stalls.
  - Every outcome is counted: available, already imported, retrieved, refused, errors, blacklisted.
  - Collectors with connection errors still get their last-collect date updated, so they don't block the queue.
- **Loop protection:**
  - Messages that GLPI itself sent are blacklisted, as are messages whose sender is the collector's own address.
  - A configurable email blacklist applies.
  - A configurable blacklist of body content (such as disclaimers) is stripped from message bodies.
- **Matching replies to tickets:**
  - First by GLPI's own Message-ID / References headers, which encode the item type and ID.
  - Otherwise by a subject tag of the form `[... #ID]`.
  - When a match is found, the email becomes a **followup** if the ticket exists and the sender is a known user with access.
  - Followups from a supplier assigned to the ticket are made private.
  - Quoted text between GLPI's notification header and footer markers is stripped. If stripping leaves nothing, only the marker lines are removed.
  - A reply to a solved or closed ticket can reopen it, under the followup reopen rules.
- **New tickets:**
  - The requester is resolved from the From or Reply-To address. If the address is unknown, it is stored as an alternative email for anonymous requesters (if allowed), or a user is created.
  - To and CC recipients can become observers, excluding blacklisted and anonymous addresses.
  - The request source is set to email.
  - Emojis are converted to shortcodes, and inline images and attachments are imported within the size limit.
  - All ticket rules, SLAs and templates then apply as for any new ticket.
- **Mail collector rules** run before ticket rules:
  - **Criteria:** collector, subject, body, From, To, Message-ID, In-Reply-To, References, X-Priority, X-Auto-Response-Suppress, Auto-Submitted, X-UCE-Status, Received, full headers, known mail domain, and the sender's profile (has profile X, has a single profile, has exactly one profile).
  - **Actions:** set the entity directly, from the email domain, from a tag, or from the user's profile entity; **refuse the email, with or without a reply to the sender**; set an external ID.
- **Archiving:** after processing, emails are moved to the accepted or refused folder, or deleted, so that failing messages are not fetched again on every run.
- **Auto-reply detection:** auto-responses are recognised by their headers, and GLPI's own auto-replies are marked as auto-replies so other systems don't answer them.

---

## Files read (all under `GLPI repo /`)
- **Statistics, dashboards and surveys:** `src/Stat.php`, `src/Glpi/Dashboard/Provider.php`, `src/Glpi/Dashboard/Grid.php`, `src/Glpi/Dashboard/Filters/`, `src/CommonITILSatisfaction.php`, `src/Entity.php` (survey, autoclose, auto-assign, alerts and autofill settings)
- **ITIL core:** `src/CommonITILObject.php`, `src/Ticket.php`, `src/Problem.php`, `src/Change.php`, `src/CommonITILObject_CommonITILObject.php`, `src/CommonITILTask.php`, `src/Planning.php`, `src/ITILFollowup.php`, `src/ITILSolution.php`, `src/PendingReason.php`, `src/CommonITILCost.php`, `src/Problem_Ticket.php`, `src/Change_Ticket.php`
- **Approvals:** `src/CommonITILValidation.php`, `src/ITIL_ValidationStep.php`, `src/ValidationStep.php`, `src/ITILValidationTemplate.php`, `src/CommonITILValidationCron.php`
- **Templates and recurrence:** `src/ITILTemplate.php`, `src/TicketTemplate.php`, `src/ChangeTemplate.php`, `src/ITILCategory.php`, `src/CommonITILRecurrent.php`, `src/TicketRecurrent.php`
- **SLAs and escalation:** `src/SLM.php`, `src/LevelAgreement.php`, `src/LevelAgreementLevel.php`, `src/SLA.php`, `src/OLA.php`, `src/SlaLevel_Ticket.php`, `src/Calendar.php`
- **Rules:** `src/Rule.php`, `src/RuleAction.php`, `src/RuleCollection.php`, `src/RuleCommonITILObject.php`, `src/RuleTicket.php`, `src/RuleMailCollector.php`
- **Assets and finance:** `src/Infocom.php`, `src/Contract.php`, `src/Alert.php`, `src/SoftwareLicense.php`, `src/Supplier.php`, `src/Budget.php`, `src/State.php`, `src/TicketCost.php`, `src/autoload/CFG_GLPI.php`, `src/Glpi/Asset/` (Capacity, CustomFieldType)
- **Mail collector:** `src/MailCollector.php`

Summary: GLPI's main strengths over iTop are the time measures stored on each record, the statistics and dashboard KPI set, approval steps with percentage thresholds, the rules engine, financial information and TCO, and pending-reason auto-resolution.
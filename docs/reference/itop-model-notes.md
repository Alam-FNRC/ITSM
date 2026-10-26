# iTop data model: clean-room feature spec for the .NET service desk

Clean-room notes on the iTop (AGPL) data model, default branch, read on 26 Oct 2026. Concepts only, in our own words; no iTop code is copied. Use with ../feature-spec.md.

## Folders read (traceability)

All under `iTop repo /datamodels/2.x/`:

- `itop-tickets/` (datamodel XML and `main.itop-tickets.php`, for impact analysis and SLT lookup)
- `itop-incident-mgmt-itil/` (datamodel XML and `dictionaries/en.dict…`, for value labels)
- `itop-request-mgmt-itil/`
- `itop-problem-mgmt/`
- `itop-knownerror-mgmt/`
- `itop-change-mgmt-itil/` and `itop-change-mgmt/` (the simpler non-ITIL change)
- `itop-config-mgmt/` (datamodel XML and English dictionary)
- `itop-endusers-devices/`
- `itop-service-mgmt/` and `itop-service-mgmt-provider/`
- `itop-sla-computation/` (only a plug-in hook: no rules of its own)
- `itop-structure/`
- `itop-bridge-cmdb-ticket/` and `itop-bridge-cmdb-services/`
- `itop-profiles-itil/` (who may trigger which transition)
- `itop-welcome-itil/` (dashboards)
- Class lists only: `itop-datacenter-mgmt/`, `itop-virtualization-mgmt/`, `itop-storage-mgmt/`, `itop-container-mgmt/`, `itop-flow-map/`

---

## 1. Common Ticket base (`itop-tickets`)

**Purpose:** an abstract parent class that every ticket type inherits from: Incident, User Request, Problem and Change.

**Key attributes**
- **ref:** a readable reference generated with a per-class prefix, such as I-000123 for incidents.
- **org_id:** the customer organisation. Required.
- **caller_id:** the requester (a Person).
- **team_id and agent_id:** the assigned team and the assigned person.
- **title and description:** both required. The description is rich text.
- **Dates:** start_date, last_update, end_date and close_date.
- **operational_status:** a summary status derived from the detailed status, with three values: ongoing, resolved or closed. Useful for reports that cover several ticket types.
- **private_log:** an append-only, timestamped journal for agents only. (Incident and User Request also have a **public_log** the caller can see.)
- **contacts_list:** a many-to-many link to Contacts. Each link has a role and a role_code:
  - *manual:* added by a user.
  - *computed:* added by impact analysis.
  - *do_not_notify:* excluded from notifications.
- **functionalcis_list:** a many-to-many link to CIs, added by the CMDB–ticket bridge. Each link has an impact_code of manual, computed or not_impacted.
- **workorders_list:** child Work Orders.

**WorkOrder:** a sub-task on a ticket. It has a name, a description, a team (required), an optional agent, start and end dates, and a log. It has two states, open and closed, and closing it stamps the end date.

**Rule: automatic impact analysis.** This runs when a ticket is created, or when its CI or contact links change. It does not run when the change comes from the end-user portal.
- It starts from the CIs linked as *manual*.
- It follows the "impacts" relationship downstream through the CMDB, at most 10 levels deep.
- It skips anything marked *not_impacted* or *do_not_notify*.
- Every CI and contact it reaches is added to the ticket as *computed*.
- Previously *computed* links that can no longer be reached are removed.
- Result: the ticket automatically lists the affected applications and business processes, and the contacts who should be notified.

**Rule: SLT lookup.** This produces the TTO and TTR targets: time-to-own and time-to-resolve.
- The system looks for SLTs that match all four of these:
  - the ticket's metric (TTO or TTR);
  - its request type (incident or service_request);
  - its priority;
  - an SLA attached to a customer contract between the ticket's organisation and the ticket's service.
- If several SLTs match, the **shortest** duration wins.
- If none match, there is no deadline.
- Elapsed time follows the configured working-hours calendar. 24x7 can be forced.

---

## 2. Incident (`itop-incident-mgmt-itil`)

**Purpose:** an unplanned interruption of a service, handled under ITIL.

**Key attributes**
- **status:** new, assigned, pending, escalated_tto, escalated_ttr, resolved or closed.
- **impact:** 1 = a department, 2 = a service, 3 = a person.
- **urgency:** 1 to 4 (Critical, High, Medium, Low).
- **priority:** 1 to 4. Always calculated by the system.
- **origin:** in_person, chat, mail, phone, portal or monitoring.
- **service_id and servicesubcategory_id:** where the incident sits in the catalogue.
- **escalation_flag** (yes or no) and **escalation_reason:** for manual escalation.
- **Dates:** assignment_date, resolution_date and last_pending_date.
- **SLA stopwatches:**
  - **tto** runs while the incident is new or escalated_tto.
  - **ttr** runs while it is new, escalated_tto, assigned or escalated_ttr.
  - **cumulatedpending** totals the time spent in pending.
  - Each stopwatch shows its deadline, its time spent, and whether it was breached and by how much.
- **time_spent:** the duration of the incident.
- **resolution_code:** assistance, bug fixed, hardware repair, software patch, system update, training or other.
- **solution:** text.
- **pending_reason:** text.
- **Parent links:** parent_incident_id, parent_request_id, parent_problem_id and parent_change_id.
- **Child lists:** child incidents and related requests.
- **user_satisfaction** (1 to 4) and **user_comment:** feedback from the caller.

**Priority matrix (impact by urgency)**

| Impact | Urgency 1 | Urgency 2 | Urgency 3 | Urgency 4 |
|---|---|---|---|---|
| 1: Department | P1 | P1 | P2 | P4 |
| 2: Service | P1 | P2 | P3 | P4 |
| 3: Person | P2 | P3 | P3 | P4 |

**Lifecycle**
- **new**
  - Assign: goes to *assigned* and stamps the assignment date.
  - TTO deadline passes (system): goes to *escalated_tto*.
  - Auto-resolve (system): goes to *resolved*.
- **escalated_tto**
  - Assign: goes to *assigned*.
  - Auto-resolve: goes to *resolved*.
- **assigned**
  - Mark pending: goes to *pending*.
  - Resolve: goes to *resolved*.
  - Reassign: stays in *assigned*.
  - TTR deadline passes (system): goes to *escalated_ttr*.
  - Auto-resolve: goes to *resolved*.
- **escalated_ttr**
  - Mark pending, resolve, reassign or auto-resolve, with the same effects as above.
- **pending**
  - Assign: goes back to *assigned*.
  - Auto-resolve: goes to *resolved*.
- **resolved**
  - Close: goes to *closed* and stamps the close date.
  - Reopen: goes back to *assigned*.
- **closed:** final state.

Resolving stamps the resolution date, records the elapsed time, and then cascades to child tickets (see the business rules below).

**Fields required at each state**
- **new:** organisation and caller.
- **assigned:** team and agent, prompted at the moment of transition.
- **pending:** pending reason.
- **resolved:** service and resolution code (both required). The user is also prompted for the sub-category and the solution text.
- **closed:** the user is prompted for satisfaction and a comment.

**SLA thresholds**
- At 75% of the TTO or TTR time, the ticket is highlighted as a warning.
- At 100%, it is highlighted as critical, and the system automatically fires the escalation transition.

**Who can trigger transitions (default ITIL profiles)**
- *Service Desk Agent:* assign only.
- *Support Agent:* assign, reassign, mark pending, resolve and close.
- *System (internal events):* timeout and auto-resolve. Users cannot trigger these.

**Business rules**
- **Parent/child cascade.** Resolving a parent incident automatically resolves every open child incident and every child user request.
  - Each child copies any resolution fields it is missing from the parent.
  - It inherits the parent's team and agent if it has none.
  - Its solution text says it was resolved from the parent ticket.
  - The cascade is protected against loops.
- **Log propagation.** When a parent's public or private log is updated, the new entries are copied into the children's logs.
- **No self-parenting.** An incident cannot be its own parent.
- **Linking to problems and changes.** An incident can be linked to a Problem (parent_problem_id) or a Change (parent_change_id). These links are references only. They do not trigger any state changes.

---

## 3. User Request (`itop-request-mgmt-itil`)

**Purpose:** a service request: a standard request for something, as opposed to a fault.

**Attributes:** the same as Incident, plus:
- **request_type:** service_request. This is used for SLT matching.
- **approver_id:** the Person who approves the request.
- **Two extra statuses:** waiting_for_approval, approved and rejected.
- **origin:** the same list as Incident, without monitoring.

**Lifecycle:** the same as Incident, plus an approval branch.
- **new**
  - Send for approval: goes to *waiting_for_approval*. The user is prompted to choose the approver.
- **waiting_for_approval**
  - Approve: goes to *approved*.
  - Reject: goes to *rejected*.
  - Auto-resolve: goes to *resolved*.
- **approved**
  - Assign: goes to *assigned*.
  - TTO deadline passes: goes to *escalated_tto*.
- **rejected**
  - Reopen: goes back to *new*.

Approval does not pause the clocks: TTO and TTR keep running in the approved state.

**Who can trigger transitions**
- *Support Agent:* can also send a request for approval.
- *Approve and reject:* only SuperUser in the default profiles. In practice, an approval extension or a custom approver profile is expected to handle this.
- *Portal user (the requester):* can close or reopen their own requests.

**Business rules**
- Same as Incident: priority matrix, child cascade, log propagation and impact analysis.
- A request can have a parent incident, a parent request, a parent problem or a parent change.

---

## 4. Problem (`itop-problem-mgmt`)

**Purpose:** the root cause behind one or more incidents.

**Key attributes**
- **status:** new, assigned, resolved or closed.
- **service and sub-category.**
- **product:** free text.
- **impact, urgency and priority:** priority is calculated with the same matrix as Incident.
- **related_change_id:** the change that fixes the problem.
- **Dates:** assignment_date and resolution_date.
- **knownerrors_list:** linked Known Errors.
- **related_incident_list and related_request_list:** the tickets that point to this problem.

**Lifecycle**
- **new**
  - Assign: goes to *assigned*. The user is prompted for organisation, description, impact and urgency.
- **assigned**
  - Reassign: stays in *assigned*.
  - Resolve: goes to *resolved*. Team and agent are required.
- **resolved**
  - Reassign: goes back to *assigned*.
  - Close: goes to *closed*. Service is required; the user is prompted for the sub-category and product.
- **closed:** final state.

**Who can trigger transitions:** the *Problem Manager* profile (assign, reassign, resolve and close). There are no SLA stopwatches on problems.

**Business rule:** incidents and requests are linked to a problem from the ticket's side (each ticket has a parent problem). Resolving a problem does **not** automatically resolve its linked tickets. Only the parent-incident cascade does that.

---

## 5. Known Error (`itop-knownerror-mgmt`)

**Purpose:** a knowledge record that captures a diagnosed fault and its workaround. It is not a ticket and has no lifecycle.

**Attributes**
- **name and organisation:** required.
- **problem_id:** optional link to the originating Problem.
- **symptom:** required.
- **root_cause, workaround and solution:** text.
- **error_code:** text.
- **domain:** Application, Network, Server or Desktop.
- **vendor, model and version.**
- **Links:** to affected CIs (each with a reason) and to Documents.

**Who can edit:** the *Problem Manager* profile. FAQ and FAQ category records belong to the same permission group.

---

## 6. Change (`itop-change-mgmt-itil`)

**Purpose:** a controlled modification to the infrastructure.

**Class tree**
- Change (abstract)
  - RoutineChange
  - ApprovedChange (abstract)
    - NormalChange
    - EmergencyChange

**Shared attributes**
- **status:** new, validated, rejected, assigned, plannedscheduled, approved, notapproved, implemented, monitored or closed.
- **reason:** the justification for a rejection or refusal.
- **requestor_id:** a Person.
- **creation_date.**
- **impact:** free-text description of the impact.
- **Supervisor:** supervisor_group_id (a Team) and supervisor_id (a Person).
- **Manager:** manager_group_id (a Team) and manager_id (a Person).
- **outage:** yes or no.
- **fallback:** the rollback plan.
- **parent_id:** links to a parent change, to model change hierarchies.
- **Related lists:** incidents, requests, problems and child changes.
- **ApprovedChange adds:** approval_date and approval_comment.
- **NormalChange also adds:** acceptance_date and acceptance_comment.

### Routine change (pre-approved, standard)
- **Flow:** new → assigned → plannedscheduled → implemented → monitored (optional) → closed.
- **Planning requires:** start and end dates, impact, outage and fallback.
- **No approval step.** The *Change Approver* profile has no transitions on routine changes.

### Normal change (full flow)
1. **new**
   - Validate: goes to *validated*. This clears the reason field.
   - Reject: goes to *rejected*.
2. **rejected**
   - Reopen: goes back to *new*. A reason is required while in this state.
3. **validated**
   - Assign: goes to *assigned*.
   - Team, supervisor group, manager group, acceptance date and acceptance comment are required.
4. **assigned**
   - Plan: goes to *plannedscheduled*.
   - Agent, supervisor and manager are prompted.
5. **plannedscheduled**
   - Approve: goes to *approved*.
   - Not approve: goes to *notapproved*.
   - Start date, end date, impact, outage and fallback are required.
6. **notapproved**
   - Replan: goes back to *plannedscheduled*. A reason is required.
7. **approved**
   - Implement: goes to *implemented*.
   - Approval date and comment are required.
8. **implemented**
   - Monitor: goes to *monitored*.
   - Finish: goes to *closed*.
9. **monitored**
   - Finish: goes to *closed*.

### Emergency change
- The same as Normal, except that **validation is skipped**: new → assigned directly.
- **Approval is still required** before implementation.

### Who can trigger transitions (the CAB equivalent)
- *Change Supervisor:* validates or rejects normal changes ("acceptance"), assigns, reopens and finishes.
- *Change Implementor:* plans, replans, implements, monitors and finishes.
- *Change Approver:* approves or refuses normal and emergency changes.

There is **no explicit CAB object**. The CAB role is played by the approver profile, combined with the "Waiting for approval" list: approved-type changes in the planned/scheduled state. A separate "Waiting for acceptance" list shows normal changes still in *new*. If we want a real CAB (meetings, votes, quorum), it is a feature we would add ourselves.

**Simple variant (`itop-change-mgmt`, non-ITIL):** one flat Change class with these states:
- new → assigned → planned
- planned → approved or rejected
- rejected → reopened to assigned
- approved → closed

---

## 7. CMDB / Configuration Items (`itop-config-mgmt`, `itop-endusers-devices`, plus infrastructure modules)

**Root class: FunctionalCI (abstract).** Attributes:
- name and organisation: required.
- description.
- **business_criticity:** high, medium or low.
- **move2production:** the go-live date.
- Links to Contacts, Documents, Application Solutions, installed software and Groups.

**PhysicalDevice (abstract)** adds:
- serial number, asset number, location, brand and model;
- status: production, implementation, stock or obsolete;
- purchase date and end-of-warranty date.

**Class hierarchy (summary)**
- **Physical devices**
  - Connectable CIs:
    - Datacenter devices: Server, NetworkDevice, StorageSystem, SANSwitch, NAS, TapeLibrary.
    - End-user devices: PC, Printer.
  - Telephony: Phone, MobilePhone, IPPhone.
  - Tablet and Peripheral.
  - Datacenter infrastructure: Rack, Enclosure, PowerSource and PDU.
- **Logical CIs**
  - ApplicationSolution and BusinessProcess.
  - SoftwareInstance: Middleware, DBServer, WebServer, PCSoftware, OtherSoftware.
  - MiddlewareInstance, DatabaseSchema and WebApplication.
  - Virtualization: Hypervisor, Farm, VirtualMachine and Cloud.
  - Containers.
  - DataFlow (from the flow-map module).
- **Supporting records (not CIs)**
  - Software catalogue: Software.
  - Patches: OS patches and software patches.
  - Licences: OS licences and software licences.
  - Network: Subnet, VLAN and network interfaces (IP and physical).
  - Groups.
  - Typologies: brand, model, OS family and version, network device type, and so on.

**The "impacts" relationship (what impact analysis follows downstream)**
- A CI impacts its **Contacts**, the **Application Solutions** it belongs to, and the **Software Instances** installed on it.
- An Application Solution impacts its **Business Processes**.
- A Middleware impacts its Middleware Instances.
- A DB Server impacts its Database Schemas.
- A Web Server impacts its Web Applications.
- A Network Device impacts the connectable CIs plugged into it.
- The virtualization, storage and datacenter modules add host→VM, storage→volume and power-chain links in the same way.

**Redundancy rules**
- An **Application Solution** can say it stays up while N of its CIs (or N% of them) are still up. By default, it needs all of them.
- Datacenter devices work the same way for their A and B power feeds.
- Impact analysis respects these rules.

**Who can edit:** the *Configuration Manager* profile, which covers CIs plus organisations, locations and contacts.

---

## 8. Service catalogue and contracts (`itop-service-mgmt`, `-provider`, `itop-bridge-cmdb-services`)

**ServiceFamily:** a top-level grouping, with a name and an icon.

**Service**
- name and provider organisation: required.
- family, description and icon.
- status: implementation, production or obsolete.
- Links to documents, contacts, customer contracts, provider contracts and the CIs that deliver the service.

**ServiceSubcategory**
- name.
- parent service: required.
- **request_type:** incident or service_request. This decides which kind of ticket the portal raises for it.
- status.

**SLA:** a name and a provider organisation, plus a list of SLTs.

**SLT (service level target)**
- **priority:** 1 to 4.
- **request_type:** incident or service_request.
- **metric:** TTO or TTR.
- **value:** an integer.
- **unit:** hours or minutes.

**Contract (abstract)**
- name and customer organisation: required.
- provider organisation: required.
- start and end dates.
- cost, currency and cost unit.
- billing frequency.
- contract type (from a typology).
- status: implementation, production or obsolete.
- links to contacts and documents.

**CustomerContract**
- Links to Services. **Each service link carries the SLA that applies.** This three-way link (customer contract + service + SLA) is what drives SLT matching.
- In the provider variant of the module, it can also link to CIs and to underpinning provider contracts.

**ProviderContract**
- Records a third-party supplier agreement.
- SLA and coverage as free text.
- Links to the CIs and services it underpins.

**DeliveryModel**
- Says which support teams and contacts (with roles) serve a group of customer organisations.
- Each Organisation points to one delivery model.

**Who can edit:** the *Service Manager* profile.

---

## 9. Organisations, Locations, Contacts and Teams (`itop-structure`)

- **Organization**
  - name and code.
  - status: active or inactive.
  - **parent_id:** a hierarchy of parent organisations, used for scoping data.
  - an overview dashboard.
- **Location**
  - name and organisation.
  - status: active or inactive.
  - address, postal code, city and country.
- **Contact (abstract)**
  - name and organisation: required.
  - email and phone.
  - status: active or inactive.
  - **notify:** yes or no.
  - function: free text.
- **Person** (a Contact) adds:
  - first name, employee number and mobile phone.
  - location.
  - **manager:** another Person.
  - picture.
- **Team** (a Contact)
  - Its members are linked to it, each with a role from the Contact Type typology.
  - It has an overview dashboard.
- **Document (abstract)**
  - Subtypes: File, Note (HTML) and Web link.
  - status: draft, published or obsolete.
  - version and type.

**Inverse lists:** a Person lists the tickets they raised as caller. A Team lists the tickets assigned to it.

---

## 10. Reporting and dashboards (`itop-welcome-itil` and each module's menus)

**Overview dashboard for each module**
- **Incidents:**
  - pie chart by priority (last 14 days);
  - bar chart of tickets per day (last 14 days);
  - tables of open incidents by status, by agent and by customer.
- **User Requests:** the same layout, plus breakdowns by request type and by class.
- **Changes:** tables by class and by status, and a daily bar chart (last 7 days).
- **Configuration, Service and Typology:** overview dashboards.

**Built-in saved lists**
- My incidents or requests: assigned to me and not resolved.
- Escalated: status escalated_tto or escalated_ttr, or the manual escalation flag set to yes.
- Open: anything not closed.
- My support calls: tickets where I am the caller.
- My changes.
- Changes waiting for approval.
- Changes waiting for acceptance.

**Other**
- A welcome page with a status header and a "my calls" list.
- Dashboards embedded on Organisation and Team records.
- An audit module: domains, categories and rules for checking data quality.

---

## Notes for our spec

1. Priority is calculated from impact and urgency. Users never set it directly.
2. SLA deadlines are found by a four-way match: customer + service + priority + request type. The shortest matching target wins.
3. Escalation happens automatically when a stopwatch reaches 100%.
4. Fields are required and prompted per state. This gives us a clean model: rules attach to transitions.
5. Change types in iTop:
   - Routine: no approval.
   - Emergency: no validation, but approval is required.
   - Normal: validation and approval.
   - There is no native CAB entity.
6. Impact analysis is a graph walk over the "impacts" relationship, with redundancy thresholds. It automatically fills in the ticket's affected CIs and the contacts to notify.
7. iTop has no first-class ISO 27001 KPIs. Its reporting is "group by" dashboards and saved queries. MTTR, SLA compliance rate and first-time-fix would have to be built on top of the stopwatch data (time spent, breached or not, and by how much).
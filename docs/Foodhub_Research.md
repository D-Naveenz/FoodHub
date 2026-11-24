# Foodhub research and educational adaptation

Research date: 2026-11-24. These are observations of official public material, not a specification of this application's behavior. Foodhub products and regional offerings can change; recheck the sources when a future feature depends on them.

## Observed capabilities

| Official source | Observation | Application interpretation |
|---|---|---|
| [Foodhub global landing page](https://global.foodhub.com/) | Foodhub directs customers to regional ordering sites and markets a low-commission offering to outlets. | Use Foodhub as domain inspiration. Do not implement commissions or copy marketing claims into business rules. |
| [Foodhub Partner API overview](https://developer.foodhub.com/) | The ecosystem includes online ordering, menus, POS, kitchen systems, and delivery tools. Online ordering supports delivery and collection. | Implement menu browsing and delivery ordering for a single takeaway. POS, kitchen, and collection workflows are outside the current scope. |
| [Where is my order?](https://help.foodhub.com/portal/en/kb/articles/how-do-i-check-where-my-order-is-9-1-2023-2) | Customers can view order tracking under Orders; delivery estimates are approximate. | Provide persisted order history and a timestamped status timeline. Do not present the simulation as live tracking or promise delivery times. |
| [Hybrid delivery on EPOS](https://deliverypartner.foodhub.com/portal/en/kb/articles/how-to-use-the-hybrid-delivery-on-your-epos) | Businesses can select delivery methods, assign drivers, and access tracking links. | Demonstrate delivery progress with a labeled local demo control. Driver assignment, dispatch, and GPS remain future possibilities. |

## Deliberate simplifications

The project uses local email/password accounts with guest browsing instead of an external identity service, one outlet instead of a restaurant marketplace, cash on delivery instead of payment processing, and locally stored food photos instead of a media service. The four normal delivery stages and early cancellation rule are project decisions already represented in the SQL script; they are not asserted to be Foodhub's exact workflow.

LKR, Sri Lankan sample dishes, and the 250.00 delivery fee are fictional demonstration settings from the existing seed script. They are not Foodhub pricing or regional service claims.

## Keeping research useful

Use the [scope](Foodhub_Project_Scope.md) for requirements and the source code/SQL for implementation facts. New research alone does not expand scope. When adopting a capability, record the user decision, learning value, acceptance criteria, and any changes to the database or UI. Keep sourced observations separate from recommendations.

# Foodhub-Inspired Food Ordering and Tracking System

Status: agreed customer-facing baseline, revised 2026-11-24. This is a living scope; later user guidance can revise it. It describes the intended application, not completed functionality. See the [architecture](Architecture.md) for the current implementation and the [roadmap](Development_Roadmap.md) for delivery order.

## Project objective

Build a customer-facing WinUI 3 desktop app backed by Microsoft SQL Server. A customer can browse a takeaway menu, place a local delivery order, follow its simulated progress, and review past orders. The project demonstrates what a customer sees and does from menu browsing through delivery.

## Scope assumptions

The agreed application covers one takeaway outlet, delivery orders, one currency, and a fixed delivery fee. Customers use local email/password accounts, with Guest access for browsing and basket previews. Internet access is not required for the intended customer workflows, but a running SQL Server must be reachable. Accounts use local password hashing and fixed-lifetime opaque sessions; optional remembered sign-in lasts seven days. This scope covers the complete project before division into versions.

LKR, a 250.00 fee, and fictional Sri Lankan sample data are provisional demonstration defaults from the SQL script. MVVM, service boundaries, and SQL-first development are provisional technical recommendations, documented separately. The in-memory basket is not required to survive restart; confirmed orders must persist.

## Functional scope

| Customer touchpoint | What the application must support |
|---|---|
| Customer profile and addresses | Register/sign in, view/edit the linked account/customer profile, choose a photo, and save/edit delivery addresses for checkout. |
| Menu browsing | Browse food by category, search by name, and view an item's photo, description, price, and availability. |
| Basket | Add available items, change quantities, remove items, and see the subtotal, delivery fee, and total update. |
| Checkout | Review the basket, contact details, delivery address, and total before confirming. Record cash on delivery as the payment method. Save the order and display a unique order number. |
| Order confirmation and tracking | Show the order number, current status, and a timestamped delivery timeline. Display **Order placed → Preparing → Out for delivery → Delivered** as the normal progression. |
| Cancellation | Let the customer cancel while the order is still at **Order placed**. Record the cancellation and show it in tracking and order history. |
| Order history | Show the authenticated customer's active and past orders. Opening an order displays the items, quantities, prices, delivery address, total, and final or current status. |

## Local data and delivery simulation

Confirming an order saves the order, its items, and the first tracking event in SQL Server. Each order retains the item names, prices, delivery fee, and address used at checkout, so the receipt stays accurate after later edits. Checkout rejects an empty basket, an unavailable item, or a missing delivery address.

A clearly labeled demo control advances a sample order one stage at a time and saves each event to SQL Server. Delivered and cancelled orders cannot advance. The control lets the presenter show every tracking state in the desktop app. Seed the database with menu items, sample profiles, and orders at different stages. Store menu photos locally.

## Out of scope

Employee management, restaurant dashboards, kitchen operations, driver assignment, live GPS, online payments, external integrations, and menu administration are outside the project. A seed script supplies the menu data.

These capabilities may be discussed as future extensions. Their appearance in [Foodhub research](Foodhub_Research.md) does not make them requirements. This project does not call Foodhub APIs.

## Presentation requirements

- Use a photo-led menu with category navigation, clear prices, and visible availability.
- Keep the basket accessible while browsing and show an itemized total before confirmation.
- Make tracking the main presentation screen. Give the current status visual prominence and show completed stages with timestamps.
- Use a consistent layout, readable type, and status labels that remain understandable without color. Show clear feedback for an empty basket, invalid checkout, successful placement, and cancellation.

## Acceptance criteria

1. Register or sign in as a customer, add food to the basket, and place an order. The confirmation shows the correct total and a unique order number.
2. Advance the order to **Delivered** and verify that the tracking timeline shows each stage in order. Cancel a separate order while it is still eligible.
3. Restart the app and open both orders from history. Their items, totals, addresses, and statuses are still available.
4. Reject empty baskets, unavailable items, and missing or wrong-owner addresses without saving a partial order. Show validation feedback in the GUI.
5. Editing current profile, menu, or store settings does not change a saved order's receipt snapshots. History is limited to the authenticated account.
6. Reject skipped, repeated, backward, and terminal-state transitions. Keep the current status and timestamped history consistent, including when actions compete.
7. Present useful loading, empty, and database-error states, with accessible status text and keyboard navigation. Label simulated delivery controls clearly.

## Account and shell slice — 2026-11-24

The approved scope now includes a guest dashboard, separate modal account window, editable account/customer profile, photos, addresses, optional remembered sign-in and appearance settings. User and Customer are distinct entities. Menu/ordering pages are scaffolded only in this slice; their acceptance criteria above describe future milestones. Email changes, password recovery/change, account deletion and staff accounts are deferred.

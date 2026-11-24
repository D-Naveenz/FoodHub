# Foodhub-Inspired Food Ordering and Tracking System

## Project objective

Build a customer-facing WinUI 3 desktop app backed by Microsoft SQL Server. A customer can browse a takeaway menu, place a local delivery order, follow its simulated progress, and review past orders. The project demonstrates what a customer sees and does from menu browsing through delivery.

## Scope assumptions

The application covers one takeaway outlet, delivery orders, one currency, and a fixed delivery fee. Customers use local profiles, and the application works without internet access. This scope covers the complete project before division into versions.

## Functional scope

| Customer touchpoint | What the application must support |
|---|---|
| Customer profile and addresses | Create, select, and edit a local customer profile. Save and edit delivery addresses and select one at checkout. |
| Menu browsing | Browse food by category, search by name, and view an item's photo, description, price, and availability. |
| Basket | Add available items, change quantities, remove items, and see the subtotal, delivery fee, and total update. |
| Checkout | Review the basket, contact details, delivery address, and total before confirming. Record cash on delivery as the payment method. Save the order and display a unique order number. |
| Order confirmation and tracking | Show the order number, current status, and a timestamped delivery timeline. Display **Order placed → Preparing → Out for delivery → Delivered** as the normal progression. |
| Cancellation | Let the customer cancel while the order is still at **Order placed**. Record the cancellation and show it in tracking and order history. |
| Order history | Show the selected customer's active and past orders. Opening an order displays the items, quantities, prices, delivery address, total, and final or current status. |

## Local data and delivery simulation

Confirming an order saves the order, its items, and the first tracking event in SQL Server. Each order retains the item names, prices, delivery fee, and address used at checkout, so the receipt stays accurate after later edits. Checkout rejects an empty basket, an unavailable item, or a missing delivery address.

A clearly labeled demo control advances a sample order one stage at a time and saves each event to SQL Server. Delivered and cancelled orders cannot advance. The control lets the presenter show every tracking state in the desktop app. Seed the database with menu items, sample profiles, and orders at different stages. Store menu photos locally.

## Out of scope

Employee management, restaurant dashboards, kitchen operations, driver assignment, live GPS, online payments, customer sign-in, external integrations, and menu administration are outside the project. A seed script supplies the menu data.

## Presentation requirements

- Use a photo-led menu with category navigation, clear prices, and visible availability.
- Keep the basket accessible while browsing and show an itemized total before confirmation.
- Make tracking the main presentation screen. Give the current status visual prominence and show completed stages with timestamps.
- Use a consistent layout, readable type, and status labels that remain understandable without color. Show clear feedback for an empty basket, invalid checkout, successful placement, and cancellation.

## Acceptance criteria

1. Create or select a customer, add food to the basket, and place an order. The confirmation shows the correct total and a unique order number.
2. Advance the order to **Delivered** and verify that the tracking timeline shows each stage in order. Cancel a separate order while it is still eligible.
3. Restart the app and open both orders from history. Their items, totals, addresses, and statuses are still available.

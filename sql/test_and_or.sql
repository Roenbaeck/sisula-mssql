-- Quick test of AND/OR operators
-- Run this against your test database after installing the updated DLL

DECLARE @template nvarchar(max) = N'
$/ foreach item in items where item.price > 50 and item.category == "electronics"
-- AND test: $item.name$ (Price: $item.price$, Category: $item.category$)
$/ endfor

$/ foreach item in items where item.category == "books" or item.category == "media"
-- OR test: $item.name$ (Category: $item.category$)
$/ endfor

$/ foreach item in items
$/ if item.price > 40 and item.stock > 0
-- IF AND test: $item.name$ available
$/ endif
$/ endfor
';

DECLARE @bindings nvarchar(max) = N'
{
  "items": [
    {"name": "Laptop", "price": 999, "category": "electronics", "stock": 10},
    {"name": "Mouse", "price": 25, "category": "electronics", "stock": 50},
    {"name": "Book", "price": 15, "category": "books", "stock": 100},
    {"name": "DVD", "price": 20, "category": "media", "stock": 0},
    {"name": "Keyboard", "price": 75, "category": "electronics", "stock": 3}
  ]
}';

DECLARE @rendered nvarchar(max) = dbo.fn_sisulate(@template, @bindings);

SELECT @rendered AS rendered_output;

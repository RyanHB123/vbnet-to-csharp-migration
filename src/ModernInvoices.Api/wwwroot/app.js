const $ = id => document.getElementById(id);
const money = amount => new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(amount);
const escapeHtml = value => String(value).replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
let products = [], customers = [], orders = [], busy = false;
let cart = [{ sku: 'MON-001', quantity: 8 }, { sku: 'DOCK-002', quantity: 2 }];
let cancellationId = null;

async function api(path, options) {
  const response = await fetch(path, options);
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.errors ? Object.values(body.errors).flat().join(' ') : body?.detail || `Request failed (${response.status}).`);
  return body;
}
const post = (path, body) => api(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) });
function notify(message) { $('notice').textContent = message; $('notice').hidden = false; }
async function action(work) {
  if (busy) return;
  busy = true; $('order-fields').disabled = true; $('error').hidden = true; $('notice').hidden = true;
  try { await work(); } catch (error) { $('error').textContent = error.message; $('error').hidden = false; }
  finally { busy = false; $('order-fields').disabled = false; }
}
function invalidate() {
  ['subtotal', 'discount', 'vat', 'total'].forEach(id => $(id).textContent = '—');
  $('place').disabled = true;
}
function basket() { return { customerId: $('customer').value, lines: cart }; }
function renderCart() {
  $('cart').innerHTML = cart.map((line, index) => `<div class="cart-row">
    <select aria-label="Product ${index + 1}" data-index="${index}" data-field="sku">${products.map(p => `<option value="${escapeHtml(p.sku)}" ${p.sku === line.sku ? 'selected' : ''}>${escapeHtml(p.name)}</option>`).join('')}</select>
    <input aria-label="Quantity ${index + 1}" data-index="${index}" data-field="quantity" type="number" min="1" max="10000" step="1" value="${line.quantity}" required>
    <button type="button" class="icon-button" data-remove="${index}" aria-label="Remove product ${index + 1}">×</button></div>`).join('');
  $('add-line').disabled = cart.length >= products.length;
}
function renderProducts() {
  const search = $('search').value.toLowerCase();
  const filtered = products.filter(p => `${p.sku} ${p.name} ${p.category}`.toLowerCase().includes(search));
  $('product-count').textContent = filtered.length;
  $('products').innerHTML = filtered.map(p => `<div class="product"><span class="product-icon" aria-hidden="true">${p.category === 'Displays' ? '▣' : p.category === 'Video' ? '◉' : '▤'}</span><div><div class="product-name">${escapeHtml(p.name)}</div><div class="product-meta">${escapeHtml(p.sku)} · ${escapeHtml(p.category)}</div></div><div class="product-price">${money(p.unitPrice)}<span class="stock-label ${p.stock <= 5 ? 'low' : ''}">${p.stock} in stock${p.stock <= 5 ? ' · Low' : ''}</span></div></div>`).join('') || '<p class="empty">No products match your search.</p>';
}
function renderOrders() {
  const filtered = orders.filter(o => $('status-filter').value === 'all' || o.status === $('status-filter').value);
  $('empty-orders').hidden = filtered.length > 0;
  $('empty-orders').textContent = orders.length ? 'No orders match this filter.' : 'Your first order starts here. Review the sample basket above, then place it.';
  $('orders').innerHTML = filtered.map(o => `<tr><td><button class="order-link" data-detail="${escapeHtml(o.id)}">${escapeHtml(o.id.slice(0, 12).toUpperCase())} ↗</button></td><td>${escapeHtml(o.customerName)}</td><td>${new Date(o.createdAt).toLocaleDateString('en-GB')}</td><td><span class="badge ${o.status === 'Cancelled' ? 'cancelled' : ''}">${escapeHtml(o.status)}</span></td><td class="numeric">${money(o.totals.total)}</td><td>${o.status === 'Placed' ? `<button class="cancel" data-cancel="${escapeHtml(o.id)}">Cancel order</button>` : '—'}</td></tr>`).join('');
}
function customerHint() {
  const customer = customers.find(c => c.id === $('customer').value);
  $('customer-hint').textContent = customer?.type === 'Trade' ? 'Trade account · 10% discount on orders of £1,000 or more, before VAT.' : 'Retail account · Standard catalogue pricing.';
}
async function refresh() {
  const result = await Promise.all([api('/api/products'), api('/api/customers'), api('/api/orders'), api('/api/reports/sales')]);
  [products, customers, orders] = result;
  const report = result[3], selected = $('customer').value;
  $('customer').innerHTML = customers.map(c => `<option value="${escapeHtml(c.id)}">${escapeHtml(c.name)} · ${escapeHtml(c.type)}</option>`).join('');
  if (customers.some(c => c.id === selected)) $('customer').value = selected;
  $('gross').textContent = money(report.grossSales); $('active').textContent = report.activeOrders;
  $('cancelled').textContent = `${report.cancelledOrders} cancelled · retained in history`;
  $('units').textContent = report.unitsSold; $('low').textContent = report.lowStockProducts;
  renderProducts(); renderOrders(); renderCart(); customerHint();
}
function updateCart(event) {
  const { index, field } = event.target.dataset;
  if (field) { cart[Number(index)][field] = field === 'quantity' ? Number(event.target.value) : event.target.value; invalidate(); }
}
$('cart').addEventListener('change', updateCart);
$('cart').addEventListener('input', updateCart);
$('cart').addEventListener('click', event => {
  if (event.target.dataset.remove !== undefined) { cart.splice(Number(event.target.dataset.remove), 1); renderCart(); invalidate(); }
});
$('add-line').addEventListener('click', () => {
  const product = products.find(p => !cart.some(line => line.sku === p.sku));
  if (product) { cart.push({ sku: product.sku, quantity: 1 }); renderCart(); invalidate(); }
});
$('customer').addEventListener('change', () => { customerHint(); invalidate(); });
$('search').addEventListener('input', renderProducts);
$('status-filter').addEventListener('change', renderOrders);
$('quote').addEventListener('click', () => {
  if (!$('order-form').reportValidity()) return;
  action(async () => {
    invalidate();
    const totals = await post('/api/orders/quote', basket());
    ['subtotal', 'discount', 'vat', 'total'].forEach(id => $(id).textContent = money(totals[id]));
    $('place').disabled = false;
  });
});
$('order-form').addEventListener('submit', event => {
  event.preventDefault();
  if ($('place').disabled) return;
  action(async () => {
    const order = await post('/api/orders', basket());
    cart = [];
    invalidate(); renderCart();
    $('success-summary').textContent = `${order.customerName}'s order for ${money(order.totals.total)} has been saved. Stock has been updated.`;
    $('success-reference').textContent = order.id;
    $('success-dialog').showModal();
    notify(`Order ${order.id.slice(0, 12).toUpperCase()} placed for ${money(order.totals.total)}. Stock and sales have been updated.`);
    try { await refresh(); }
    catch { $('error').textContent = 'Your order was saved, but the dashboard could not refresh. Reload the page to see the latest stock and history.'; $('error').hidden = false; }
  });
});
$('orders').addEventListener('click', event => {
  const { cancel, detail } = event.target.dataset;
  if (cancel && !busy) { cancellationId = cancel; $('cancel-dialog').showModal(); }
  if (detail) {
    const order = orders.find(o => o.id === detail);
    $('detail-body').innerHTML = `<p>${escapeHtml(order.id)}</p><p>${escapeHtml(order.customerName)} · ${escapeHtml(order.status)}</p><div class="table-wrap"><table><thead><tr><th>Product</th><th>Qty</th><th>Unit price</th></tr></thead><tbody>${order.lines.map(line => `<tr><td>${escapeHtml(line.name)}</td><td>${line.quantity}</td><td>${money(line.unitPrice)}</td></tr>`).join('')}</tbody></table></div><div class="totals"><div><span>Subtotal</span><span>${money(order.totals.subtotal)}</span></div><div><span>Discount</span><span>${money(order.totals.discount)}</span></div><div><span>VAT</span><span>${money(order.totals.vat)}</span></div><div class="total"><span>Total</span><strong>${money(order.totals.total)}</strong></div></div>`;
    $('order-detail').showModal();
  }
});
$('close-detail').addEventListener('click', () => $('order-detail').close());
$('success-close').addEventListener('click', () => $('success-dialog').close());
$('success-history').addEventListener('click', () => {
  $('success-dialog').close();
  $('status-filter').value = 'all'; renderOrders();
  $('history').scrollIntoView({ behavior: 'smooth' });
  $('status-filter').focus({ preventScroll: true });
});
$('keep-order').addEventListener('click', () => $('cancel-dialog').close());
$('confirm-cancel').addEventListener('click', () => {
  const id = cancellationId;
  $('cancel-dialog').close();
  if (id) action(async () => {
    await post(`/api/orders/${encodeURIComponent(id)}/cancel`); invalidate(); await refresh(); notify('Order cancelled. Stock has been restored.');
  });
});
action(refresh);

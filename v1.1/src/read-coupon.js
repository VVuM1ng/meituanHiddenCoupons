function readCoupon(expectedName, poiId) {
  const origin = 'https://offsiteact.meituan.com';
  const path = '/web/hoae/collection_waimai_v8/index.html';
  const isStorePage = url => url.origin === origin && url.pathname === path
    && url.searchParams.get('poi_id_str') === poiId;
  if (!isStorePage(new URL(location.href))) return null;

  const normalize = text => text.normalize('NFKC').replace(/\s/g, '');
  const name = normalize(expectedName);
  const documents = [document];
  for (const page of documents) {
    for (const frame of page.querySelectorAll('iframe')) {
      const nested = frame.contentDocument;
      if (nested && isStorePage(new URL(nested.URL))) documents.push(nested);
    }
  }

  const cardSelector = '.poiInfo, [class*="style-module__poiInfoK___"]';
  const cardName = card => card.querySelector('.poiDetail > .name, [class*="style-module__name___"]')
    ?.textContent.trim() || '';
  const allCards = documents.flatMap(page => [...page.querySelectorAll(cardSelector)]);
  const firstName = allCards.map(cardName).find(Boolean);
  const cards = allCards.filter(card => normalize(cardName(card)) === name);
  if (cards.length === 0) return firstName ? { firstName } : null;

  let hasEmptyCouponBox = false;
  for (const card of cards) {
    const coupon = card.querySelector('.poiCouponBox .poiCoupon');
    if (coupon?.querySelector('.title')?.textContent.trim() === '专享商家券') {
      const value = coupon.querySelector('.valueInfo > .value')?.textContent.trim();
      if (/^\d+(?:\.\d+)?$/.test(value || '')) return { matched: true, amount: Number(value) };
      return { matched: true };
    }
    const ownLabel = [...card.querySelectorAll('[class*="style-module__tags___"] span')]
      .map(tag => tag.textContent.trim())
      .find(tag => tag.startsWith('专享商家券'));
    if (ownLabel) {
      const amount = ownLabel.match(/减\s*(\d+(?:\.\d+)?)/)?.[1];
      return amount ? { matched: true, amount: Number(amount) } : { matched: true };
    }
    hasEmptyCouponBox ||= card.querySelector('.poiCouponBox')?.childElementCount === 0;
  }

  // Some shop cards display a single generic offer. A filled coupon section is not proof of no merchant coupon.
  return { matched: true, noCoupon: hasEmptyCouponBox };
}

namespace Nikkiward.ViewModels;

public static class JournalWebCaptureScripts
{
    public const string Overview = """
        (() => {
          const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
          const asNodes = value => Array.isArray(value)
            ? value.filter(Boolean)
            : value
              ? [value]
              : [];
          const descendants = value => asNodes(value).flatMap(element => [
            element,
            ...Array.from(element.querySelectorAll?.('*') || [])
          ]);
          const textLines = element => asNodes(element)
            .map(node => node?.innerText || node?.textContent || '')
            .join('\n')
            .split(/\r?\n/)
            .map(normalize)
            .filter(Boolean);
          const lines = (document.body?.innerText || '')
            .split(/\r?\n/)
            .map(normalize)
            .filter(Boolean);
          const isAccountLine = value =>
            /搭配师|昵称|\bUID\b|退出登录|登录账号|账号信息|手机号|验证码/i.test(value);
          const valueNear = (labels, pattern) => {
            for (const label of labels) {
              const index = lines.findIndex(line => line === label || line.includes(label));
              if (index < 0) continue;
              const sameLine = normalize(lines[index].replace(label, ''));
              const candidates = [
                sameLine,
                lines[index - 1],
                lines[index + 1],
                lines[index - 2],
                lines[index + 2]
              ].map(normalize).filter(Boolean);
              const match = candidates.find(value => pattern.test(value));
              if (match) return { value: match, source: `text-near:${label}` };
            }
            return { value: null, source: null };
          };
          const normalizeUrl = raw => {
            try {
              if (!raw) return null;
              const url = new URL(raw, location.href);
              if (!/^https:$/i.test(url.protocol)) return null;
              const host = url.hostname.toLowerCase();
              if (!(host === 'nuanpaper.com' || host.endsWith('.nuanpaper.com') ||
                    host === 'papegames.com' || host.endsWith('.papegames.com'))) return null;
              url.search = '';
              url.hash = '';
              return url.href;
            } catch (_) {
              return null;
            }
          };
          const backgroundUrls = element => {
            const urls = [];
            const pattern = /url\((['"]?)(.*?)\1\)/gi;
            let backgroundImage = '';
            try { backgroundImage = getComputedStyle(element).backgroundImage || ''; } catch (_) { }
            for (const match of backgroundImage.matchAll(pattern)) {
              const url = normalizeUrl(match[2]);
              if (url) urls.push(url);
            }
            return urls;
          };
          const imageUrls = element => {
            const urls = [];
            const addImage = image => {
              const raw = image?.currentSrc || image?.src || image?.getAttribute?.('data-src') ||
                image?.getAttribute?.('data-original') || image?.getAttribute?.('data-lazy-src');
              const url = normalizeUrl(raw);
              if (url) urls.push(url);
            };
            for (const image of descendants(element).filter(node =>
              node?.tagName?.toLowerCase?.() === 'img')) addImage(image);
            for (const candidate of descendants(element).slice(0, 900)) {
              urls.push(...backgroundUrls(candidate));
            }
            return Array.from(new Set(urls));
          };
          const collectModuleNodes = wrapper => {
            if (!wrapper) return [];
            const nodes = [];
            let current = wrapper;
            for (let count = 0; current && count < 64; count++) {
              if (current !== wrapper && current.getAttribute?.('data-key') !== null) break;
              nodes.push(current);
              current = current.nextElementSibling;
            }
            return nodes.length > 1 || !wrapper.parentElement
              ? nodes
              : [wrapper.parentElement];
          };
          const findDataKeyModule = keys => {
            const wanted = new Set(keys || []);
            let best = null;
            let bestScore = -1;
            for (const wrapper of Array.from(document.querySelectorAll('[data-key]'))) {
              if (!wanted.has(wrapper.getAttribute('data-key'))) continue;
              const nodes = collectModuleNodes(wrapper);
              const localLines = textLines(nodes);
              const urls = imageUrls(nodes);
              const metricCount = localLines.filter(isMetricValue).length;
              const rect = wrapper.getBoundingClientRect?.();
              const visible = (rect?.width || 0) > 0 || (rect?.height || 0) > 0;
              const score = (visible ? 100000 : 0) + Math.min(localLines.length, 120) * 4 +
                Math.min(urls.length, 12) * 300 + Math.min(metricCount, 12) * 40;
              if (score > bestScore) {
                best = { wrapper, nodes, score };
                bestScore = score;
              }
            }
            return best;
          };
          const metricValuePattern = /^[-+]?\d+(?:[.,]\d+)?(?:\s*(?:%|h|hours?|kg|公斤|小时|天|次|件|套|个|颗|层|张|只|枚|组|点|级))?$/i;
          const metricRatioPattern = /^[-+]?\d+(?:[.,]\d+)?\s*\/\s*[-+]?\d+(?:[.,]\d+)?(?:\s*(?:%|h|hours?|kg|公斤|小时|天|次|件|套|个|颗|层|张|只|枚|组|点|级))?$/i;
          const metricInlinePattern = /^(.+?)\s*[:：]\s*([-+]?\d+(?:[.,]\d+)?(?:\s*(?:%|h|hours?|kg|公斤|小时|天|次|件|套|个|颗|层|张|只|枚|组|点|级))?)$/i;
          const metricNoisePattern = /^(?:已完成|已集齐|已解锁|查看全部|努力挖掘中|未挑战|到底了|配色|头部|手部|脚部|剩余.*恢复满格|挑战刷新时间.*)$/i;
          const isMetricValue = value => {
            const normalized = normalize(value);
            return metricValuePattern.test(normalized) || metricRatioPattern.test(normalized) ||
              metricInlinePattern.test(normalized);
          };
          const metricLabelNear = (moduleLines, index, title) => {
            for (let offset = 1; offset <= 6; offset++) {
              for (const candidateIndex of [index - offset, index + offset]) {
                const candidate = normalize(moduleLines[candidateIndex]);
                if (!candidate || candidate === title || isMetricValue(candidate) ||
                    metricNoisePattern.test(candidate) || isAccountLine(candidate)) continue;
                return candidate;
              }
            }
            return '';
          };
          const extractMetrics = (moduleLines, title) => {
            const metrics = [];
            const addMetric = (label, value, source) => {
              const normalizedLabel = normalize(label);
              const normalizedValue = normalize(value);
              if (!normalizedLabel || !normalizedValue || normalizedLabel.length > 48 ||
                  isAccountLine(normalizedLabel) || !isMetricValue(normalizedValue)) return;
              if (!metrics.some(metric => metric.label === normalizedLabel && metric.value === normalizedValue)) {
                metrics.push({ label: normalizedLabel, value: normalizedValue, source });
              }
            };
            for (let index = 0; index < moduleLines.length && metrics.length < 8; index++) {
              const line = normalize(moduleLines[index]);
              if (!line) continue;
              const inline = line.match(metricInlinePattern);
              if (inline) {
                addMetric(inline[1], inline[2], `module-line:${index}`);
                continue;
              }
              if (metricValuePattern.test(line) && /^\s*\/\s*$/.test(moduleLines[index + 1] || '') &&
                  metricValuePattern.test(normalize(moduleLines[index + 2]))) {
                addMetric(
                  metricLabelNear(moduleLines, index, title),
                  `${line} / ${normalize(moduleLines[index + 2])}`,
                  `module-lines:${index}-${index + 2}`);
                continue;
              }
              if (metricRatioPattern.test(line)) {
                addMetric(metricLabelNear(moduleLines, index, title), line, `module-line:${index}`);
                continue;
              }
              const valueFirst = line.match(/^([-+]?\d+(?:[.,]\d+)?(?:\s*(?:%|h|hours?|kg|公斤|小时|天|次|件|套|个|颗|层|张|只|枚|组|点|级))?)\s+(.+)$/i);
              const labelFirst = line.match(/^(.+?)\s+([-+]?\d+(?:[.,]\d+)?(?:\s*(?:%|h|hours?|kg|公斤|小时|天|次|件|套|个|颗|层|张|只|枚|组|点|级))?)$/i);
              if (valueFirst) {
                addMetric(valueFirst[2], valueFirst[1], `module-line:${index}`);
              } else if (labelFirst) {
                addMetric(labelFirst[1], labelFirst[2], `module-line:${index}`);
              } else if (metricValuePattern.test(line)) {
                addMetric(metricLabelNear(moduleLines, index, title), line, `module-line:${index}`);
              }
            }
            return metrics.slice(0, 6);
          };
          const moduleContainerScore = (candidate, title) => {
            if (!candidate || candidate === document.body ||
                candidate.closest?.('nav, [role="navigation"]')) return Number.NEGATIVE_INFINITY;
            const localLines = textLines(candidate);
            if (localLines.length < 2 || localLines.length > 120) return Number.NEGATIVE_INFINITY;
            const urls = imageUrls(candidate);
            const metricCount = localLines.filter(isMetricValue).length;
            const interactiveOnly = candidate.matches?.('a[href], button, [role="link"]') &&
              localLines.length <= 2 && urls.length === 0;
            if (interactiveOnly) return Number.NEGATIVE_INFINITY;
            const titlePresent = localLines.some(line => line === title || line.startsWith(`${title} `));
            if (!titlePresent) return Number.NEGATIVE_INFINITY;
            return 24 + Math.min(localLines.length, 24) + Math.min(urls.length, 8) * 8 +
              Math.min(metricCount, 8) * 4 - Math.max(0, localLines.length - 48) * 2;
          };
          const chooseModuleContainer = titleElement => {
            let candidate = titleElement;
            let best = null;
            for (let depth = 0; candidate && depth < 12; depth++, candidate = candidate.parentElement) {
              const score = moduleContainerScore(candidate, normalize(titleElement.textContent));
              if (!best || score > best.score) best = { element: candidate, score };
            }
            return best;
          };
          const moduleTitles = [
            { title: '日程便利贴', anchor: 'schedule-note', dataKeys: ['scheduleNote'] },
            { title: '探索总览', anchor: 'exploration-overview', dataKeys: ['exploreOverview'] },
            { title: '奇想札记', anchor: 'inspiration-sketches', dataKeys: ['fancyNotes'] },
            { title: '祝福闪光', anchor: 'blessing-sparkle', dataKeys: ['blessingSparkle'] },
            { title: '心愿共鸣', anchor: 'wish-resonance', dataKeys: ['wishReasonace', 'wishResonance'] },
            { title: '共鸣衣橱', anchor: 'resonance-wardrobe', dataKeys: ['miracleClothesPress'] },
            { title: '奇迹之冠', anchor: 'miracle-crown', dataKeys: ['miracleCrown'] }
          ];
          const allElements = Array.from(document.querySelectorAll('body *'));
          const resourceMap = new Map();
          const addResource = (raw, altText, source, role = null, nodeKey = null, order = 0) => {
            const url = normalizeUrl(raw);
            if (!url || resourceMap.has(url) || resourceMap.size >= 2048) return;
            resourceMap.set(url, {
              url,
              source,
              altText: normalize(altText).slice(0, 240) || null,
              role,
              nodeKey,
              order: Math.max(0, Number(order) || 0)
            });
          };
          const sections = moduleTitles.map(definition => {
            const title = definition.title;
            const dataKeyModule = findDataKeyModule(definition.dataKeys);
            let titleElement = dataKeyModule?.wrapper || null;
            let moduleNodes = dataKeyModule?.nodes || [];
            if (!titleElement) {
              const selected = allElements
                .filter(element => {
                  const text = normalize(element.textContent);
                  if (!(text === title || text.startsWith(`${title} `))) return false;
                  return !Array.from(element.children || []).some(child =>
                    normalize(child.textContent) === title);
                })
                .map(candidateTitle => ({
                  titleElement: candidateTitle,
                  container: chooseModuleContainer(candidateTitle)
                }))
                .filter(candidate => candidate.container?.score > Number.NEGATIVE_INFINITY)
                .sort((left, right) => right.container.score - left.container.score)[0];
              titleElement = selected?.titleElement || null;
              moduleNodes = selected ? [selected.container.element] : [];
            }
            if (!titleElement || moduleNodes.length === 0) return null;
            const visibleTitle = textLines(titleElement)[0] || title;
            const moduleLines = textLines(moduleNodes)
              .filter(line => line !== title && line !== visibleTitle &&
                !definition.dataKeys.includes(line) && !isAccountLine(line));
            const routeElement = descendants(moduleNodes)
              .find(element => element?.matches?.('a[href]')) ||
              titleElement.closest('a[href]');
            let route = null;
            try {
              const href = routeElement?.getAttribute('href');
              if (href) {
                const routeUrl = new URL(href, location.href);
                if (routeUrl.origin === location.origin && routeUrl.pathname.startsWith('/tools/journal')) {
                  route = routeUrl.pathname;
                }
              }
            } catch (_) { }
            const urls = imageUrls(moduleNodes);
            const imageUrl = urls[0] || null;
            const sectionKey = route ? `route:${route.toLowerCase()}` : `anchor:${definition.anchor}`;
            const source = dataKeyModule
              ? `module:${sectionKey}:data-key:${definition.dataKeys[0]}`
              : `module:${sectionKey}:title-container`;
            for (const url of urls) addResource(url, title, `module-art:${sectionKey}`);
            return {
              sectionKey,
              source,
              title,
              text: moduleLines.slice(0, 18).join(' · '),
              route,
              imageUrl,
              metrics: extractMetrics(moduleLines, title)
            };
          }).filter(Boolean);
          const semanticBlocks = [];
          const semanticBySection = new Map();
          const referencesBySection = new Map();
          const addSemanticBlock = (sectionKey, block) => {
            const normalized = {
              key: normalize(block.key || `${sectionKey}:${semanticBlocks.length}`),
              parentKey: normalize(block.parentKey) || null,
              kind: normalize(block.kind) || 'text',
              order: Math.max(0, Number(block.order) || 0),
              label: normalize(block.label) || null,
              value: normalize(block.value) || null,
              status: normalize(block.status) || null,
              unit: normalize(block.unit) || null,
              current: normalize(block.current) || null,
              total: normalize(block.total) || null,
              resourceUrl: normalizeUrl(block.resourceUrl),
              source: normalize(block.source) || `semantic:${sectionKey}`
            };
            if (!normalized.key) return;
            semanticBlocks.push(normalized);
            if (!semanticBySection.has(sectionKey)) semanticBySection.set(sectionKey, []);
            semanticBySection.get(sectionKey).push(normalized);
            if (normalized.resourceUrl) {
              if (!referencesBySection.has(sectionKey)) referencesBySection.set(sectionKey, []);
              referencesBySection.get(sectionKey).push({
                url: normalized.resourceUrl,
                role: normalized.kind,
                nodeKey: normalized.key,
                order: normalized.order,
                source: normalized.source
              });
            }
          };
          const parseRatio = value => {
            const match = normalize(value).match(/([-+]?\d+(?:[.,]\d+)?)\s*\/\s*([-+]?\d+(?:[.,]\d+)?)/);
            return match ? { current: match[1], total: match[2], value: `${match[1]}/${match[2]}` } :
              { current: null, total: null, value: normalize(value) };
          };
          const firstText = (root, selectors) => {
            for (const selector of selectors) {
              const value = normalize(root?.querySelector?.(selector)?.innerText || '');
              if (value) return value;
            }
            return '';
          };
          const firstImage = (root, selectors) => {
            for (const selector of selectors) {
              const image = root?.querySelector?.(selector);
              const url = normalizeUrl(image?.currentSrc || image?.src || image?.getAttribute?.('data-src'));
              if (url) return url;
            }
            return null;
          };
          const addModuleReferences = (sectionKey, urls, role) => {
            const refs = referencesBySection.get(sectionKey) || [];
            urls.forEach((url, order) => {
              const normalized = normalizeUrl(url);
              if (!normalized) return;
              addResource(normalized, sectionKey, `module-art:${sectionKey}`, role, sectionKey, order);
              if (!refs.some(reference => reference.url === normalized && reference.order === order)) {
                refs.push({ url: normalized, role, nodeKey: sectionKey, order, source: `module-art:${sectionKey}` });
              }
            });
            referencesBySection.set(sectionKey, refs);
          };

          const explorationSectionKey = 'anchor:exploration-overview';
          const explorationCards = Array.from(document.querySelectorAll('[class*="card-khjEPV"]'));
          explorationCards.forEach((card, cardIndex) => {
            const regionName = firstText(card, ['[class*="cardTitle-"]', '[class*="cardTitle"]', '[class*="title-"]']) ||
              `区域 ${cardIndex + 1}`;
            const regionKey = `${explorationSectionKey}:${normalize(regionName)}`;
            addSemanticBlock(explorationSectionKey, {
              key: regionKey,
              kind: 'explore-group',
              order: cardIndex,
              label: regionName,
              status: /已集齐/.test(normalize(card.innerText)) ? '已集齐' : null,
              source: `explore-group:${regionName}`
            });
            Array.from(card.querySelectorAll('[class*="collectItem-"]')).forEach((item, itemIndex) => {
              const name = firstText(item, ['[class*="name-"]']) || `收集物 ${itemIndex + 1}`;
              const ratio = parseRatio(firstText(item, ['[class*="ratio-"]']));
              const status = firstText(item, ['[class*="finished-"]']) || null;
              const imageUrl = firstImage(item, ['img']);
              const key = `${regionKey}:${normalize(name)}:${itemIndex}`;
              if (imageUrl) addResource(imageUrl, name, `explore-item:${regionName}`, 'explore-item', key, itemIndex);
              addSemanticBlock(explorationSectionKey, {
                key,
                parentKey: regionName,
                kind: 'explore-item',
                order: cardIndex * 100 + itemIndex,
                label: name,
                value: ratio.value,
                current: ratio.current,
                total: ratio.total,
                status,
                resourceUrl: imageUrl,
                source: `explore-item:${regionName}:${name}`
              });
            });
          });

          const notesSectionKey = 'anchor:inspiration-sketches';
          const notebookBoard = document.querySelector('[class*="board-"]');
          if (notebookBoard) {
            Array.from(notebookBoard.querySelectorAll('[class*="listItem-"]')).forEach((item, index) => {
              const itemLines = textLines(item);
              const label = itemLines[0] || '';
              const value = itemLines.slice(1).join(' · ');
              const imageUrl = firstImage(item, ['img']);
              const key = `${notesSectionKey}:${normalize(label)}:${index}`;
              if (imageUrl) addResource(imageUrl, label, 'notebook-record', 'notebook-record', key, index);
              addSemanticBlock(notesSectionKey, {
                key,
                kind: 'notebook-record',
                order: index,
                label,
                value,
                resourceUrl: imageUrl,
                source: `notebook-record:${label}`
              });
            });
            const boardLines = textLines(notebookBoard);
            for (const label of ['阅读物图鉴', '生物图鉴']) {
              const index = boardLines.findIndex(line => line === label || line.includes(label));
              if (index < 0) continue;
              const value = boardLines.slice(index, index + 3).find(line => /\d/.test(line) && line !== label) || '';
              addSemanticBlock(notesSectionKey, {
                key: `${notesSectionKey}:${label}`,
                kind: 'notebook-summary',
                order: index,
                label,
                value,
                source: `notebook-summary:${label}`
              });
            }
          }

          const wishSectionKey = 'anchor:wish-resonance';
          const wishLabels = ['共鸣次数', '限定五星', '限定四星', '常驻五星', '四星数'];
          wishLabels.forEach((label, index) => {
            const lineIndex = lines.findIndex(line => line === label || line.startsWith(`${label} `));
            if (lineIndex < 0) return;
            const candidates = [lines[lineIndex].replace(label, ''), lines[lineIndex + 1], lines[lineIndex - 1], lines[lineIndex + 2]]
              .map(normalize).filter(Boolean);
            const value = candidates.find(candidate => /\d/.test(candidate)) || '';
            addSemanticBlock(wishSectionKey, {
              key: `${wishSectionKey}:${label}`,
              kind: 'wish-stat',
              order: index,
              label,
              value,
              source: `wish-stat:${label}`
            });
          });
          const wishRatios = lines.filter(line => /^\d+\s*\/\s*\d+$/.test(line));
          wishRatios.slice(0, 2).forEach((value, index) => addSemanticBlock(wishSectionKey, {
            key: `${wishSectionKey}:sets:${index}`,
            kind: 'wish-set-ratio',
            order: 10 + index,
            label: '集齐/套装',
            value,
            ...parseRatio(value),
            source: `wish-set-ratio:${index}`
          }));

          const wardrobeSectionKey = 'anchor:resonance-wardrobe';
          const wardrobeCards = Array.from(document.querySelectorAll('[class*="cardWrpaper-"]')).slice(0, 12);
          wardrobeCards.forEach((card, cardIndex) => {
            const patchTitle = firstText(card, ['[class*="patch-"] [class*="title-"]', '[class*="patch-"]']);
            const outfitTitle = firstText(card, ['[class*="name-"]']);
            const summary = firstText(card, ['[class*="detail-"]']);
            const average = summary.match(/平均(?:共鸣次数|共鸣)\s*[:：]?\s*([\d.]+)/i)?.[1] || '';
            const total = summary.match(/总计?共鸣次数\s*[:：]?\s*([\d.]+)/i)?.[1] || '';
            const leftText = firstText(card, ['[class*="left-"]']);
            const ratio = parseRatio(leftText);
            const remaining = firstText(card, ['[class*="remainTimeWrapper-"]']);
            const coverUrl = firstImage(card, ['[class*="mainSuit-"] img', 'img[class*="img-"]']);
            const cardKey = `${wardrobeSectionKey}:${cardIndex}`;
            if (coverUrl) addResource(coverUrl, outfitTitle, 'wardrobe-cover', 'cover', cardKey, cardIndex);
            addSemanticBlock(wardrobeSectionKey, {
              key: cardKey,
              kind: 'wardrobe-card',
              order: cardIndex,
              label: outfitTitle,
              value: `平均共鸣次数: ${average} · 总计共鸣次数: ${total}`,
              current: ratio.current,
              total: ratio.total,
              status: /已集齐/.test(leftText) ? '已集齐' : leftText,
              unit: remaining,
              resourceUrl: coverUrl,
              source: `wardrobe-card:${patchTitle}:${outfitTitle}`
            });
          });

          const blessingSectionKey = 'anchor:blessing-sparkle';
          const blessingModule = findDataKeyModule(['blessingSparkle']);
          const blessingNodes = blessingModule?.nodes || [];
          const blessingCard = descendants(blessingNodes).find(element => {
            const className = typeof element?.className === 'string' ? element.className : '';
            return className.includes('card-') && element.querySelector?.('img');
          });
          if (blessingCard) {
            const blessingLines = textLines(blessingCard);
            const title = blessingLines[0] || '祝福闪光';
            const categories = blessingLines.slice(1).join(' · ');
            const coverUrl = firstImage(blessingCard, ['[class*="thumb-"]', 'img']);
            const cardKey = `${blessingSectionKey}:card`;
            if (coverUrl) addResource(coverUrl, title, 'blessing-card', 'cover', cardKey, 0);
            addSemanticBlock(blessingSectionKey, {
              key: cardKey,
              kind: 'blessing-card',
              order: 0,
              label: title,
              value: categories,
              resourceUrl: coverUrl,
              source: `blessing-card:${title}`
            });
            const seenBlessingUrls = new Set();
            Array.from(blessingCard.querySelectorAll('img')).forEach((image, imageIndex) => {
              const imageUrl = normalizeUrl(image.currentSrc || image.src || image.getAttribute('data-src'));
              const className = typeof image.className === 'string' ? image.className : '';
              if (!imageUrl || seenBlessingUrls.has(imageUrl)) return;
              seenBlessingUrls.add(imageUrl);
              if (className.includes('thumb-') || imageUrl === coverUrl) return;
              const role = className.includes('swatch-') ? 'color' :
                className.includes('ball-') ? 'part' : 'decoration';
              const kind = role === 'color' ? 'blessing-color' :
                role === 'part' ? 'blessing-part' : 'blessing-resource';
              const key = `${blessingSectionKey}:${role}:${imageIndex}`;
              addResource(imageUrl, title, `blessing-${role}`, role, key, imageIndex);
              addSemanticBlock(blessingSectionKey, {
                key,
                kind,
                order: imageIndex + 1,
                label: role === 'color' ? `配色 ${imageIndex + 1}` :
                  role === 'part' ? `部件 ${imageIndex + 1}` : '祝福素材',
                resourceUrl: imageUrl,
                source: `blessing-${role}:${title}`
              });
            });
          }

          const crownSectionKey = 'anchor:miracle-crown';
          ['搭配赛', '巅峰赛'].forEach((label, index) => {
            const lineIndex = lines.findIndex(line => line.includes(label));
            if (lineIndex < 0) return;
            addSemanticBlock(crownSectionKey, {
              key: `${crownSectionKey}:${label}`,
              kind: 'crown-stat',
              order: index,
              label,
              value: lines.slice(lineIndex, lineIndex + 3).join(' · '),
              source: `crown-stat:${label}`
            });
          });

          const scheduleSectionKey = 'anchor:schedule-note';
          ['活跃能量', '朝夕心愿', '美鸭梨挖掘', '心之突破幻境'].forEach((label, index) => {
            const lineIndex = lines.findIndex(line => line === label || line.startsWith(`${label} `));
            if (lineIndex < 0) return;
            const nearby = lines.slice(lineIndex, lineIndex + 5);
            const ratioLine = nearby.find(line => /\d+\s*\/\s*\d+/.test(line));
            const ratio = parseRatio(ratioLine || nearby.find(line => /\d/.test(line)) || '');
            addSemanticBlock(scheduleSectionKey, {
              key: `${scheduleSectionKey}:${label}`,
              kind: 'schedule-task',
              order: index,
              label,
              value: nearby.slice(1).join(' · '),
              current: ratio.current,
              total: ratio.total,
              status: nearby.find(line => /已完成|努力挖掘中|未挑战|恢复满格/.test(line)) || null,
              source: `schedule-task:${label}`
            });
          });

          for (const section of sections) {
            const sectionResources = Array.from(resourceMap.values())
              .filter(resource => resource.source === `module-art:${section.sectionKey}`);
            const refs = referencesBySection.get(section.sectionKey) || [];
            sectionResources.forEach((resource, resourceIndex) => {
              if (refs.some(reference => reference.url === resource.url)) return;
              refs.push({
                url: resource.url,
                role: resource.role || 'module-art',
                nodeKey: resource.nodeKey || section.sectionKey,
                order: Number.isFinite(resource.order) ? resource.order : resourceIndex,
                source: resource.source
              });
            });
            referencesBySection.set(section.sectionKey, refs);
          }
          for (const section of sections) {
            section.blocks = semanticBySection.get(section.sectionKey) || [];
            section.resourceReferences = referencesBySection.get(section.sectionKey) || [];
          }
          for (const image of Array.from(document.images || [])) {
            addResource(
              image.currentSrc || image.src || image.getAttribute('data-src') ||
                image.getAttribute('data-original') || image.getAttribute('data-lazy-src'),
              image.alt || image.getAttribute('aria-label'),
              'document-image', 'image', null, 0);
          }
          for (const element of allElements.slice(0, 2500)) {
            for (const url of backgroundUrls(element)) {
              addResource(url, element.getAttribute('aria-label') || element.title, 'computed-background', 'background');
            }
          }
          const loginDays = valueNear(['登录总天数'], /^\d{1,6}$/);
          const gameHours = valueNear(
            ['游戏时长'],
            /^\d+(?:[.,]\d+)?\s*(?:h|小时|hours?)$/i);
          const outfitCount = valueNear(['服装数量'], /^\d{1,7}$/);
          const momoCloakCount = valueNear(['大喵斗篷', '大喵斗篷数量'], /^\d{1,7}$/);
          const sketchCount = valueNear(['设计图', '设计图数量'], /^\d{1,7}$/);
          return JSON.stringify({
            schemaVersion: 2,
            capturedAtUtc: new Date().toISOString(),
            pageTitle: normalize(document.title),
            sourcePagePath: location.pathname,
            loginDays: loginDays.value,
            loginDaysSource: loginDays.source,
            gameHours: gameHours.value,
            gameHoursSource: gameHours.source,
            outfitCount: outfitCount.value,
            outfitCountSource: outfitCount.source,
            momoCloakCount: momoCloakCount.value,
            momoCloakCountSource: momoCloakCount.source,
            sketchCount: sketchCount.value,
            sketchCountSource: sketchCount.source,
            summaryText: [loginDays.value, gameHours.value, outfitCount.value, momoCloakCount.value, sketchCount.value]
              .filter(Boolean)
              .join(' · '),
            summarySource: 'overview-stat-fields',
            sections,
            contentBlocks: semanticBlocks,
            resources: Array.from(resourceMap.values()),
            sanitizedVisibleText: lines.filter(line => !isAccountLine(line)).slice(0, 600)
          });
        })()
        """;

    public const string PrepareOverview = """
        (() => {
          const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
          const bodyText = document.body?.innerText || '';
          const visibleLineCount = bodyText
            .split(/\r?\n/)
            .map(normalize)
            .filter(Boolean)
            .length;
          const stableNodeKeys = new Set(
            Array.from(document.querySelectorAll('[data-key], [data-testid], [data-section], [id], main section, main article, main h1, main h2'))
              .map((element, index) =>
                element.getAttribute('data-key') ||
                element.getAttribute('data-testid') ||
                element.getAttribute('data-section') ||
                element.id ||
                `${element.tagName.toLowerCase()}:${index}`)
              .filter(Boolean));
          const pendingImageCount = Array.from(document.images || [])
            .filter(image => {
              const bounds = image.getBoundingClientRect();
              const visible = bounds.bottom >= 0 && bounds.top <= window.innerHeight;
              return visible && Boolean(image.currentSrc || image.src) && !image.complete;
            })
            .length;
          return JSON.stringify({
            documentReady: document.readyState === 'complete',
            imageCount: document.images?.length || 0,
            pendingImageCount,
            stableNodeKeyCount: stableNodeKeys.size,
            textLength: bodyText.length,
            visibleLineCount
          });
        })()
        """;

    public const string PrepareResonance = """
        (() => {
          for (const details of Array.from(document.querySelectorAll('details'))) {
            details.open = true;
          }
          const scrollables = Array.from(document.querySelectorAll('*'))
            .filter(element => element.scrollHeight > element.clientHeight + 200)
            .sort((left, right) => right.scrollHeight - left.scrollHeight)
            .slice(0, 8);
          for (const element of scrollables) {
            element.scrollTop = element.scrollHeight;
          }
          window.scrollTo(0, document.documentElement.scrollHeight || document.body.scrollHeight);
          return JSON.stringify({
            scrollHeight: Math.max(
              document.documentElement.scrollHeight || 0,
              document.body?.scrollHeight || 0),
            cardCount: document.querySelectorAll('[class*="cardWrpaper-"]').length,
            imageCount: document.querySelectorAll('[class*="cardWrpaper-"] img').length,
            markerCount: Array.from(document.querySelectorAll('body *'))
              .filter(element => /总(?:计)?共鸣次数|平均共鸣次数|平均共鸣/.test(element.textContent || ''))
              .length
          });
        })()
        """;

    public const string Resonance = """
        (() => {
          const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
          const normalizeIdentity = value => normalize(value)
            .toLowerCase()
            .replace(/[^\p{L}\p{N}._-]+/gu, '-')
            .replace(/^-+|-+$/g, '')
            .slice(0, 160);
          const capturedAtUtc = new Date().toISOString();
          const linesOf = element => (element?.innerText || '')
            .split(/\r?\n/)
            .map(normalize)
            .filter(Boolean);
          const normalizeUrl = raw => {
            try {
              if (!raw) return null;
              const url = new URL(raw, location.href);
              if (!/^https:$/i.test(url.protocol)) return null;
              const host = url.hostname.toLowerCase();
              if (!(host === 'nuanpaper.com' || host.endsWith('.nuanpaper.com') ||
                    host === 'papegames.com' || host.endsWith('.papegames.com'))) return null;
              url.search = '';
              url.hash = '';
              return url.href;
            } catch (_) {
              return null;
            }
          };
          const backgroundUrls = element => {
            const urls = [];
            const pattern = /url\((['"]?)(.*?)\1\)/gi;
            let backgroundImage = '';
            try { backgroundImage = getComputedStyle(element).backgroundImage || ''; } catch (_) { }
            for (const match of backgroundImage.matchAll(pattern)) {
              const url = normalizeUrl(match[2]);
              if (url) urls.push(url);
            }
            return urls;
          };
          const totalPullsPattern = /总(?:计)?共鸣次数|共鸣总数/;
          const averagePullsPattern = /平均共鸣次数|平均共鸣/;
          const isMarker = element => {
            const ownText = normalize(element.textContent);
            if (!totalPullsPattern.test(ownText) && !averagePullsPattern.test(ownText)) return false;
            return !Array.from(element.children || []).some(child =>
              totalPullsPattern.test(normalize(child.textContent)) ||
              averagePullsPattern.test(normalize(child.textContent)));
          };
          const findBanner = marker => {
            let candidate = marker;
            for (let depth = 0; candidate && depth < 12; depth++, candidate = candidate.parentElement) {
              const text = normalize(candidate.innerText);
              const hasMetrics = totalPullsPattern.test(text) && averagePullsPattern.test(text);
              const imageCount = (candidate.querySelectorAll?.('img').length || 0) +
                backgroundUrls(candidate).length;
              if (hasMetrics && imageCount >= 1 && text.length <= 6000) return candidate;
            }
            return null;
          };
          const allElements = Array.from(document.querySelectorAll('body *'));
          const candidates = [];
          for (const marker of allElements.filter(isMarker)) {
            const banner = findBanner(marker);
            if (banner && !candidates.includes(banner)) candidates.push(banner);
          }
          const readNear = (bannerLines, labels) => {
            for (const label of labels) {
              const index = bannerLines.findIndex(line => line === label || line.includes(label));
              if (index < 0) continue;
              const sameLine = normalize(bannerLines[index].replace(label, ''))
                .replace(/^[:：]\s*/, '');
              const values = [sameLine, bannerLines[index + 1], bannerLines[index - 1]]
                .map(normalize)
                .filter(Boolean);
              const numeric = values.find(value => /\d/.test(value));
              if (numeric) return numeric;
              const textValue = values.find(value =>
                !labels.some(candidate => value.includes(candidate)));
              if (textValue) return textValue;
            }
            return '';
          };
          const itemCount = image => {
            let candidate = image;
            for (let depth = 0; candidate && depth < 6; depth++, candidate = candidate.parentElement) {
              const text = normalize(candidate.innerText);
              if (!text || text.length > 180) continue;
              const explicit = text.match(/(?:获得(?:次数)?|已获得|已拥有|拥有)\s*[:：x×]?\s*(\d{1,4})/i);
              if (explicit) return Number(explicit[1]);
              const compact = text.match(/(?:^|\s)[x×]\s*(\d{1,4})(?:\s|$)/i);
              if (compact) return Number(compact[1]);
            }
            return 0;
          };
          const itemDetails = image => {
            let candidate = image;
            for (let depth = 0; candidate && depth < 6; depth++, candidate = candidate.parentElement) {
              const lines = linesOf(candidate)
                .filter(line => line.length <= 120)
                .slice(0, 24);
              if (!lines.length) continue;
              const explicitName = lines.find(line =>
                /^(?:名称|物品|服装)\s*[:：]\s*\S+/.test(line));
              const rarityLine = lines.find(line =>
                /(?:稀有度|星级)\s*[:：]?\s*[1-5]\s*(?:星|★)?/i.test(line) ||
                /^[1-5]\s*(?:星|★)$/.test(line) ||
                /^[★☆]{1,5}$/.test(line));
              const pullLine = lines.find(line =>
                /(?:第\s*\d{1,4}\s*抽|抽数\s*[:：]?\s*\d{1,4})/i.test(line));
              const itemId = normalize(
                candidate.getAttribute?.('data-item-id') ||
                candidate.getAttribute?.('data-id') ||
                image.getAttribute?.('data-item-id') ||
                image.getAttribute?.('data-id')) || null;
              const nameMatch = explicitName?.match(/^(?:名称|物品|服装)\s*[:：]\s*(.+)$/);
              const rarityMatch = rarityLine?.match(/([1-5])\s*(?:星|★)?/);
              const starMatch = rarityLine?.match(/[★☆]{1,5}/);
              const pullMatch = pullLine?.match(/(?:第\s*(\d{1,4})\s*抽|抽数\s*[:：]?\s*(\d{1,4}))/i);
              if (itemId || nameMatch || rarityMatch || starMatch || pullMatch) {
                return {
                  itemId,
                  itemName: nameMatch ? normalize(nameMatch[1]) : null,
                  rarity: rarityMatch
                    ? Number(rarityMatch[1])
                    : starMatch
                      ? starMatch[0].length
                      : null,
                  pullNumber: pullMatch ? Number(pullMatch[1] || pullMatch[2]) : null
                };
              }
            }
            return { itemId: null, itemName: null, rarity: null, pullNumber: null };
          };
          let totalItems = 0;
          const banners = candidates.slice(0, 256).map((container, bannerIndex) => {
            const bannerLines = linesOf(container);
            const metricPattern = /平均共鸣次数|平均共鸣|总(?:计)?共鸣次数|共鸣总数|完成|剩余|获得次数|已获得|已拥有/;
            const titleLines = bannerLines.filter(line =>
              !metricPattern.test(line) &&
              !/^\d+(?:[.,]\d+)?(?:\s*(?:次|天|小时|h|%))?$/i.test(line) &&
              line.length >= 2 && line.length <= 80);
            const images = Array.from(container.querySelectorAll('img'))
              .map(image => ({
                element: image,
                url: normalizeUrl(
                  image.currentSrc || image.src || image.getAttribute('data-src') ||
                    image.getAttribute('data-original') || image.getAttribute('data-lazy-src')),
                area: Math.max(
                  (image.naturalWidth || 0) * (image.naturalHeight || 0),
                  Math.round((image.getBoundingClientRect?.().width || 0) *
                    (image.getBoundingClientRect?.().height || 0)))
              }))
              .filter(entry => entry.url);
            const backgroundEntries = [container, ...Array.from(container.querySelectorAll('*'))]
              .flatMap(element => {
                const rect = element.getBoundingClientRect?.();
                const area = Math.round((rect?.width || 0) * (rect?.height || 0));
                return backgroundUrls(element).map(url => ({ url, area }));
              });
            const largestImage = images.slice().sort((left, right) => right.area - left.area)[0];
            const largestBackground = backgroundEntries
              .slice()
              .sort((left, right) => right.area - left.area)[0];
            const coverEntry = largestImage &&
              largestImage.area >= (largestBackground?.area || 0)
                ? largestImage
                : null;
            const coverImageUrl = coverEntry?.url || largestBackground?.url || largestImage?.url || '';
            const patchTitle = titleLines[0] || `共鸣记录 ${bannerIndex + 1}`;
            const outfitTitle = titleLines[1] || '';
            const poolId = normalizeIdentity(
              container.getAttribute?.('data-pool-id') ||
              container.getAttribute?.('data-banner-id') ||
              patchTitle) || `pool-${bannerIndex + 1}`;
            const poolName = outfitTitle || patchTitle;
            const items = [];
            for (const entry of images) {
              if (items.length + totalItems >= 3000) break;
              if (entry.element === coverEntry?.element) continue;
              if (entry.url === coverImageUrl) continue;
              const details = itemDetails(entry.element);
              const slotIndex = items.length;
              const itemIdentity = details.itemId || entry.url || `slot-${slotIndex}`;
              items.push({
                stableId: poolId + '|' + normalizeIdentity(itemIdentity) + '|' + slotIndex,
                timestampUtc: capturedAtUtc,
                poolId,
                poolName,
                itemId: details.itemId,
                itemName: details.itemName,
                rarity: details.rarity,
                pullNumber: details.pullNumber,
                imageUri: entry.url,
                slotIndex,
                obtainCount: itemCount(entry.element),
                imageUrl: entry.url
              });
            }
            totalItems += items.length;
            return {
              poolId,
              poolName,
              patchTitle,
              outfitTitle,
              averagePulls: readNear(bannerLines, ['平均共鸣次数', '平均共鸣']),
              totalPulls: readNear(bannerLines, ['总计共鸣次数', '总共鸣次数', '共鸣总数']),
              completionText: readNear(bannerLines, ['完成状态', '完成']),
              remainingText: readNear(bannerLines, ['剩余时间', '剩余']),
              coverImageUrl,
              items
            };
          });
          return JSON.stringify({
            schemaVersion: 1,
            capturedAtUtc,
            sourcePagePath: location.pathname,
            banners
          });
        })()
        """;

    public const string ResonanceFull = """
        (() => {
          const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
          const normalizeIdentity = value => normalize(value)
            .toLowerCase()
            .replace(/[^\p{L}\p{N}._-]+/gu, '-')
            .replace(/^-+|-+$/g, '')
            .slice(0, 160);
          const normalizeUrl = raw => {
            try {
              if (!raw) return null;
              const url = new URL(raw, location.href);
              if (!/^https:$/i.test(url.protocol)) return null;
              const host = url.hostname.toLowerCase();
              if (!(host === 'nuanpaper.com' || host.endsWith('.nuanpaper.com') ||
                    host === 'papegames.com' || host.endsWith('.papegames.com'))) return null;
              url.search = '';
              url.hash = '';
              return url.href;
            } catch (_) { return null; }
          };
          const textOf = (root, selectors) => {
            for (const selector of selectors) {
              const value = normalize(root?.querySelector?.(selector)?.innerText || '');
              if (value) return value;
            }
            return '';
          };
          const urlOf = node => normalizeUrl(
            node?.currentSrc || node?.src || node?.getAttribute?.('data-src') ||
            node?.getAttribute?.('data-original') || node?.getAttribute?.('data-lazy-src'));
          const firstUrl = (root, selectors) => {
            for (const selector of selectors) {
              const url = urlOf(root?.querySelector?.(selector));
              if (url) return url;
            }
            return '';
          };
          const parseCount = value => {
            const match = normalize(value).match(/\d[\d,]*/);
            return match ? Number(match[0].replace(/,/g, '')) : 0;
          };
          const parseRatio = value => {
            const match = normalize(value).match(/(\d+)\s*\/\s*(\d+)/);
            return match ? { current: match[1], total: match[2], value: `${match[1]}/${match[2]}` } :
              { current: null, total: null, value: normalize(value) };
          };
          const capturedAtUtc = new Date().toISOString();
          const cards = Array.from(document.querySelectorAll('[class*="cardWrpaper-"]'));
          const banners = cards.slice(0, 256).map((card, bannerIndex) => {
            const patchTitle = textOf(card, ['[class*="patch-"] [class*="title-"]', '[class*="patch-"]']) ||
              `共鸣记录 ${bannerIndex + 1}`;
            const outfitTitle = textOf(card, ['[class*="name-"]']);
            const detail = textOf(card, ['[class*="detail-"]']);
            const averagePulls = detail.match(/平均(?:共鸣次数|共鸣)\s*[:：]?\s*([\d.]+)/i)?.[1] || '';
            const totalPulls = detail.match(/总计?共鸣次数\s*[:：]?\s*([\d.]+)/i)?.[1] || '';
            const leftText = textOf(card, ['[class*="left-"]']);
            const ratio = parseRatio(leftText);
            const starNode = card.querySelector('[class*="stars-"]');
            const starClass = typeof starNode?.className === 'string' ? starNode.className : '';
            const rarity = /5/.test(starClass) || card.querySelector('[class*="indicatorStar5-"]') ? 5 : 4;
            const coverImageUrls = Array.from(card.querySelectorAll('[class*="mainSuit-"] img'))
              .map(urlOf).filter(Boolean).filter((url, index, values) => values.indexOf(url) === index);
            const coverImageUrl = coverImageUrls[0] || firstUrl(card, ['img']);
            const poolId = normalizeIdentity(card.getAttribute('data-pool-id') || patchTitle) ||
              `pool-${bannerIndex + 1}`;
            const poolName = outfitTitle || patchTitle;
            const seenSlotUrls = new Set();
            const items = Array.from(card.querySelectorAll('img[class*="smallCardImg-"]'))
              .map((image, slotIndex) => {
                const holder = image.parentElement;
                const holderClass = typeof holder?.className === 'string' ? holder.className : '';
                const count = parseCount(holder?.querySelector?.('[class*="count-"]')?.textContent || '0');
                const itemUrl = urlOf(image) || '';
                const itemRarity = holderClass.includes('Pink') ? 5 : holderClass.includes('Purple') ? 4 : rarity;
                const itemId = normalizeIdentity(
                  image.getAttribute('data-item-id') || image.getAttribute('data-id') || itemUrl) ||
                  `slot-${slotIndex}`;
                return {
                  stableId: `${poolId}|${itemId}|${slotIndex}`,
                  timestampUtc: null,
                  poolId,
                  poolName,
                  itemId,
                  itemName: normalize(image.alt || image.title || '') || null,
                  rarity: itemRarity,
                  pullNumber: null,
                  imageUri: itemUrl,
                  slotIndex,
                  obtainCount: count,
                  imageUrl: itemUrl,
                  statusText: count > 0 ? '已拥有' : '未拥有',
                  resourceRole: 'slot'
                };
              })
              .filter(item => {
                if (!item.imageUrl || seenSlotUrls.has(item.imageUrl)) return false;
                seenSlotUrls.add(item.imageUrl);
                item.slotIndex = seenSlotUrls.size - 1;
                item.stableId = `${poolId}|${item.itemId}|${item.slotIndex}`;
                return true;
              });
            return {
              poolId,
              poolName,
              patchTitle,
              outfitTitle,
              averagePulls,
              totalPulls,
              completionText: /已集齐/.test(leftText) ? '已集齐' : ratio.value,
              remainingText: textOf(card, ['[class*="remainTimeWrapper-"]']),
              rarity,
              coverImageUrl,
              coverImageUrls,
              items
            };
          });
          return JSON.stringify({
            schemaVersion: 1,
            capturedAtUtc,
            sourcePagePath: location.pathname,
            banners
          });
        })()
        """;
}

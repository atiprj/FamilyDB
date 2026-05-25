const EXCLUDED_TOKENS = [
  "schedule",
  "sheet",
  "insulation",
  "pattern",
  "masse",
  "mass"
] as const;

function categoryNameMatchesToken(categoryName: string, token: string): boolean {
  const name = categoryName.trim();
  if (name.toLowerCase() === token) {
    return true;
  }

  if (name.toLowerCase().startsWith(token) && name.length > token.length) {
    const next = name[token.length];
    if (!next || !/[a-z0-9]/i.test(next)) {
      return true;
    }
  }

  if (` ${name.toLowerCase()} `.includes(` ${token} `)) {
    return true;
  }

  if (token === "mass" && name.toLowerCase() === "masses") {
    return true;
  }

  return false;
}

export function isExcludedCategoryName(
  categoryName: string | null | undefined
): boolean {
  if (!categoryName?.trim()) {
    return false;
  }

  const name = categoryName.trim();
  return EXCLUDED_TOKENS.some((token) => categoryNameMatchesToken(name, token));
}

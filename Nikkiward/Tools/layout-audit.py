#!/usr/bin/env python3
"""Static XAML layout audit.

Catches classes of defect that only surface as visual glitches at runtime, so
they can be caught by a build gate instead of by eye:

  MISSING_ROWDEFS / MISSING_COLDEFS
      A child sets Grid.Row/Grid.Column beyond the parent's declared
      definitions. WinUI silently clamps to index 0, stacking the children on
      top of each other.

  CAPTION_COLLISION
      A hit-testable element overlaps the window caption buttons, which own the
      top-right 160x48 of the window. Overlapping them makes minimize/maximize
      /close unclickable.

Exit code is non-zero when any problem is found.
"""

import glob
import os
import sys
import xml.etree.ElementTree as ET

P = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

# The caption buttons occupy the top-right of the window. Anything
# hit-testable inside this band steals their clicks.
CAPTION_WIDTH = 160.0
CAPTION_HEIGHT = 48.0


def name_of(el):
    return el.get(X + "Name") or "(anonymous)"


def count(grid, kind):
    node = grid.find(f"{P}Grid.{kind}Definitions")
    return len(list(node)) if node is not None else 0


def span(child, axis):
    index = child.get(f"Grid.{axis}")
    length = child.get(f"Grid.{axis}Span")
    if not (index and index.isdigit()):
        return 0
    return int(index) + (int(length) if length and length.isdigit() else 1)


def check_grids(tree, path, problems):
    for grid in tree.iter():
        if not grid.tag.endswith("}Grid"):
            continue
        declared = {"Row": count(grid, "Row"), "Column": count(grid, "Column")}
        for axis in ("Row", "Column"):
            needed = 0
            for child in grid:
                if child.tag.startswith(P + "Grid."):
                    continue
                needed = max(needed, span(child, axis))
            # An undeclared Grid still has one implicit row and column.
            if needed > max(declared[axis], 1):
                problems.append(
                    f"{path}: MISSING_{axis.upper()}DEFS in Grid "
                    f"{name_of(grid)} - children need {needed} "
                    f"{axis.lower()}s, {declared[axis]} declared"
                )


def parse_thickness(value):
    parts = [p for p in value.replace(",", " ").split() if p]
    try:
        nums = [float(p) for p in parts]
    except ValueError:
        return None
    if len(nums) == 1:
        return nums * 4
    if len(nums) == 2:
        return [nums[0], nums[1], nums[0], nums[1]]
    if len(nums) == 4:
        return nums
    return None


# Containers that re-base their children's coordinates, so a right/top
# alignment inside one says nothing about the window's caption band.
SCOPE_BREAKERS = (
    "DataTemplate",
    "ItemsControl",
    "ItemsView",
    "ListView",
    "GridView",
    "Flyout",
    "MenuFlyout",
    "ContentDialog",
    "ScrollViewer",
    "Expander",
    "TeachingTip",
    "ToolTip",
)


def window_relative(el, parents):
    """True when this element's top-right alignment resolves against the window.

    Walks the ancestor chain: any templated/scrolled/sized container in between
    means the element is positioned inside that container instead, so its
    margin cannot be compared against window coordinates.
    """
    node = parents.get(el)
    while node is not None:
        tag = node.tag.split("}")[-1]
        if tag in SCOPE_BREAKERS:
            return False
        if "." in tag:  # property-element such as Grid.RowDefinitions
            return False
        # A container with a fixed size clips its children into its own box.
        if node.get("Width") or node.get("Height"):
            return False
        padding = node.get("Padding")
        if padding and parse_thickness(padding) is None:
            return False
        # A container that does not fill its parent is likewise self-contained.
        if node.get("HorizontalAlignment") in ("Left", "Center", "Right"):
            return False
        if node.get("VerticalAlignment") in ("Top", "Center", "Bottom"):
            return False
        node = parents.get(node)
    return True


def check_caption_clearance(tree, path, problems):
    """Flag top-right anchored interactive elements that reach the caption band.

    Only elements whose alignment provably resolves against the window are
    considered, so this stays quiet rather than guessing about nested content.
    """
    root = tree.getroot()
    parents = {child: parent for parent in root.iter() for child in parent}
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag not in ("Button", "StackPanel", "Grid", "Border", "ToggleButton"):
            continue
        if el.get("IsHitTestVisible") == "False":
            continue
        if el.get("HorizontalAlignment") != "Right":
            continue
        if el.get("VerticalAlignment") != "Top":
            continue
        margin = parse_thickness(el.get("Margin", "0"))
        if margin is None:
            continue
        right, top = margin[2], margin[1]
        # Reaches into the caption band horizontally and vertically.
        if right >= CAPTION_WIDTH or top >= CAPTION_HEIGHT:
            continue
        if not window_relative(el, parents):
            continue
        problems.append(
            f"{path}: CAPTION_COLLISION - {tag} {name_of(el)} "
            f"anchored top-right at margin right={right:g} top={top:g} "
            f"overlaps the {CAPTION_WIDTH:g}x{CAPTION_HEIGHT:g} caption "
            f"button region"
        )


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    targets = sorted(
        path
        for path in glob.glob(os.path.join(root, "**", "*.xaml"), recursive=True)
        if not {"bin", "obj"}.intersection(
            os.path.relpath(path, root).split(os.sep)
        )
    )
    if not targets:
        print("layout-audit: no XAML files found", file=sys.stderr)
        return 2

    problems = []
    for path in targets:
        rel = os.path.relpath(path, root).replace("\\", "/")
        try:
            tree = ET.parse(path)
        except ET.ParseError as error:
            problems.append(f"{rel}: PARSE_ERROR - {error}")
            continue
        check_grids(tree, rel, problems)
        check_caption_clearance(tree, rel, problems)

    for problem in problems:
        print(problem)

    print(
        f"layout-audit: {len(targets)} file(s) checked, "
        f"{len(problems)} problem(s)"
    )
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

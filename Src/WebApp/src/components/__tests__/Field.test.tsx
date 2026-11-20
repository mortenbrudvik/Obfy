import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import Field from "../Field";

describe("Field", () => {
  it("rendersLabelHelpAndControl_andTiesThemTogether", () => {
    render(
      <Field label="Output folder" help="Leave empty to write next to the input.">
        {(props) => <input {...props} />}
      </Field>,
    );

    const input = screen.getByLabelText("Output folder");
    expect(input.tagName).toBe("INPUT");
    const help = screen.getByText("Leave empty to write next to the input.");
    expect(input.getAttribute("aria-describedby")).toBe(help.id);
  });

  it("showsAnError_andMarksTheControlInvalid", () => {
    render(
      <Field label="Id" help="A build number." error="Watermark needs an id.">
        {(props) => <input {...props} />}
      </Field>,
    );

    const input = screen.getByLabelText("Id");
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByText("Watermark needs an id.")).toBeTruthy();
  });
});

import { Extension, Node, mergeAttributes } from "@tiptap/core";
import Image from "@tiptap/extension-image";

declare module "@tiptap/core" {
  interface Commands<ReturnType> {
    spacing: {
      setLetterSpacing: (value: string) => ReturnType;
      setWordSpacing: (value: string) => ReturnType;
    };
    answerSpace: {
      insertAnswerSpace: (attrs: { lines: number; ruled: boolean; grid: boolean }) => ReturnType;
    };
    pageBreak: {
      insertPageBreak: () => ReturnType;
    };
    imageRole: {
      setImageRole: (role: string) => ReturnType;
    };
  }
}

/** Per-selection letter and word spacing (V2 §9). Stored on the same inline "textStyle" mark as colour and size, so it
 * round-trips through the HTML the exporters read. */
export const Spacing = Extension.create({
  name: "spacing",

  addGlobalAttributes() {
    return [
      {
        types: ["textStyle"],
        attributes: {
          letterSpacing: {
            default: null,
            parseHTML: (el) => (el as HTMLElement).style.letterSpacing || null,
            renderHTML: (attrs) => (attrs.letterSpacing ? { style: `letter-spacing: ${attrs.letterSpacing}` } : {}),
          },
          wordSpacing: {
            default: null,
            parseHTML: (el) => (el as HTMLElement).style.wordSpacing || null,
            renderHTML: (attrs) => (attrs.wordSpacing ? { style: `word-spacing: ${attrs.wordSpacing}` } : {}),
          },
        },
      },
    ];
  },

  addCommands() {
    return {
      setLetterSpacing: (value) => ({ chain }) => chain().setMark("textStyle", { letterSpacing: value === "0" ? null : value }).run(),
      setWordSpacing: (value) => ({ chain }) => chain().setMark("textStyle", { wordSpacing: value === "0" ? null : value }).run(),
    };
  },
});

function containerNode(name: string, dataType: string, withQuestionAttrs: boolean) {
  return Node.create({
    name,
    group: "block",
    content: "block+",
    defining: true,

    addAttributes() {
      if (!withQuestionAttrs) return {};
      return {
        questionId: {
          default: null,
          parseHTML: (el) => (el as HTMLElement).getAttribute("data-question-id"),
          renderHTML: (attrs) => (attrs.questionId ? { "data-question-id": attrs.questionId } : {}),
        },
        points: {
          default: null,
          parseHTML: (el) => (el as HTMLElement).getAttribute("data-points"),
          renderHTML: (attrs) => (attrs.points !== null && attrs.points !== undefined ? { "data-points": attrs.points } : {}),
        },
      };
    },

    parseHTML() {
      return [{ tag: `div[data-type="${dataType}"]` }];
    },

    renderHTML({ HTMLAttributes }) {
      return ["div", mergeAttributes(HTMLAttributes, { "data-type": dataType }), 0];
    },
  });
}

/** One question of the test. Its id and points travel with it so the validator can tell which question the teacher edited. */
export const QuestionBlock = containerNode("questionBlock", "question", true);
/** The shared reading passage a group of questions refers to. */
export const StimulusBlock = containerNode("stimulusBlock", "stimulus", false);
/** A free "bloque" the teacher can wrap around content (V2 §9). */
export const BoxBlock = containerNode("boxBlock", "block", false);

/** Space to answer: blank, ruled or grid lines (V2 §9 "espacios de respuesta"). */
export const AnswerSpace = Node.create({
  name: "answerSpace",
  group: "block",
  atom: true,
  selectable: true,
  draggable: true,

  addAttributes() {
    return {
      lines: {
        default: 3,
        parseHTML: (el) => Number((el as HTMLElement).getAttribute("data-lines") ?? 3),
        renderHTML: (attrs) => ({ "data-lines": attrs.lines }),
      },
      ruled: {
        default: false,
        parseHTML: (el) => (el as HTMLElement).getAttribute("data-ruled") === "true",
        renderHTML: (attrs) => ({ "data-ruled": attrs.ruled ? "true" : "false" }),
      },
      grid: {
        default: false,
        parseHTML: (el) => (el as HTMLElement).getAttribute("data-grid") === "true",
        renderHTML: (attrs) => ({ "data-grid": attrs.grid ? "true" : "false" }),
      },
    };
  },

  parseHTML() {
    return [{ tag: 'div[data-type="answer-space"]' }];
  },

  renderHTML({ HTMLAttributes, node }) {
    const lines = Number(node.attrs.lines) || 1;
    return ["div", mergeAttributes(HTMLAttributes, { "data-type": "answer-space", style: `height: ${lines * 26}px` }), "espacio para responder"];
  },

  addCommands() {
    return {
      insertAnswerSpace: (attrs) => ({ commands }) => commands.insertContent({ type: this.name, attrs }),
    };
  },
});

export const PageBreak = Node.create({
  name: "pageBreak",
  group: "block",
  atom: true,
  selectable: true,
  draggable: true,

  parseHTML() {
    return [{ tag: 'div[data-type="page-break"]' }];
  },

  renderHTML({ HTMLAttributes }) {
    return ["div", mergeAttributes(HTMLAttributes, { "data-type": "page-break" })];
  },

  addCommands() {
    return {
      insertPageBreak: () => ({ commands }) => commands.insertContent({ type: this.name }),
    };
  },
});

/** Image that remembers what it is for (V2 §12): decorative, access aid, clue, or "reveals the answer" — the last two are
 * flagged at validation so the teacher reviews them before exporting. Also keeps a teacher-set width. */
export const RoleImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      role: {
        default: "content",
        parseHTML: (el) => (el as HTMLElement).getAttribute("data-role") ?? "content",
        renderHTML: (attrs) => ({ "data-role": attrs.role }),
      },
      width: {
        default: null,
        parseHTML: (el) => (el as HTMLElement).getAttribute("width"),
        renderHTML: (attrs) => (attrs.width ? { width: attrs.width } : {}),
      },
    };
  },

  addCommands() {
    return {
      ...this.parent?.(),
      setImageRole: (role: string) => ({ commands }) => commands.updateAttributes("image", { role }),
    };
  },
});
